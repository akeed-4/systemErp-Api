using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

/// <summary>
/// بيع نقطة البيع. لا يبني قيوداً ولا حركات مخزون بنفسه: يحوّل السلة إلى فاتورة مبيعات عبر ISalesInvoiceService
/// (مخزون + قيد + QR)، ثم يحفظ ما هو خاص بالكاشير (الوردية، الكوبون، الولاء، الباقي) — كل ذلك في معاملة واحدة.
/// </summary>
public class PosSaleService : IPosSaleService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ISalesInvoiceService _invoices;
    private readonly ISalesReturnService _returns;
    private readonly ITransactionRunner _tx;
    private readonly IAccountingPostingService _posting;
    private readonly IPermissionService _permissions;

    public PosSaleService(ErpDbContext db, ICurrentUser user, ISalesInvoiceService invoices, ISalesReturnService returns, ITransactionRunner tx,
        IAccountingPostingService posting, IPermissionService permissions)
    {
        _db = db; _user = user; _invoices = invoices; _returns = returns; _tx = tx; _posting = posting; _permissions = permissions;
    }

    public Task<PosTransactionDto> CheckoutAsync(CheckoutRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token)
                ?? throw new ConflictException(Messages.OpenShiftBeforeSale);
            var settings = await _db.Set<PosInvoiceSettings>().AsNoTracking().FirstOrDefaultAsync(token) ?? new PosInvoiceSettings();

            // 1-5) التسعير: السلة، العروض والخصم اليدوي، الكوبون، الولاء، الضريبة
            var cart = await PriceCartAsync(r, strict: true, shift, token);
            var (lines, products, lineDiscounts, coupon, couponDiscount, customer, loyalty, loyaltyDiscount, pointsUsed, priced) =
                (cart.Lines, cart.Products, cart.LineDiscounts, cart.Coupon, cart.CouponDiscount, cart.Customer, cart.Loyalty, cart.LoyaltyDiscount, cart.PointsUsed, cart.Priced);
            var total = priced.GrandTotal;

            // 6) الدفع: بطرق جدول طرق الدفع إن أُرسلت، وإلا بالأنواع الثابتة (واجهات قديمة)
            var terminal = await RequireTerminalPaymentAsync(r, shift, total, token);
            var configured = r.Payments.Count > 0 ? await ResolveConfiguredPaymentAsync(r.Payments, total, customer, token) : null;
            var (cash, card, mada, apple, change) = configured?.Buckets ?? ResolvePayment(r, total, customer);
            var method = configured?.Method ?? EffectiveMethod(r.PaymentMethod, cash, card, mada, apple);

            // 6ب) دفع إلكتروني مكتمل عبر Paymob: يطابق الإجمالي ويُستهلك مرة واحدة
            OnlinePayment? online = null;
            if (r.OnlinePaymentId.HasValue)
            {
                if (method is not (PosPaymentMethod.Card or PosPaymentMethod.Mada or PosPaymentMethod.ApplePay))
                    throw new ValidationFailedException(Messages.OnlinePaymentMethodValues);
                online = await _db.Set<OnlinePayment>().FirstOrDefaultAsync(p => p.Id == r.OnlinePaymentId, token)
                    ?? throw new NotFoundException(Messages.OnlinePaymentNotFound);
                if (online.Purpose != OnlinePaymentPurpose.PosSale || online.Status != OnlinePaymentStatus.Paid)
                    throw new ConflictException(Messages.OnlinePaymentNotCompleted);
                if (online.ConsumedAt != null) throw new ConflictException(Messages.PaymentAlreadyUsed);
                if (online.Amount != total) throw new ConflictException(string.Format(Messages.OnlinePaymentAmountMismatch, online.Amount, total));
            }

            // 7) الفاتورة (مخزون + قيد + QR) عبر محرك الفوترة المركزي
            var standard = string.Equals(r.InvoiceType ?? settings.DefaultInvoiceType, "standard", StringComparison.OrdinalIgnoreCase);
            var vat = string.IsNullOrWhiteSpace(r.CustomerTaxNumber) ? customer?.VatNumber : r.CustomerTaxNumber;
            if (standard && string.IsNullOrWhiteSpace(vat)) throw new ValidationFailedException(Messages.StandardInvoiceRequiresCustomerVat);
            var invoiceDto = new CreateInvoiceDto
            {
                Kind = InvoiceKind.Sales, InvoiceType = standard ? InvoiceType.TaxInvoice : InvoiceType.Simplified,
                WarehouseId = shift.WarehouseId ?? settings.WarehouseId,
                PartyId = customer?.Id, PartyName = r.CustomerName ?? customer?.NameAr ?? "عميل نقدي POS",
                PartyVatNumber = vat, PartyPhone = r.CustomerPhone ?? customer?.Phone,
                InvoiceDiscount = couponDiscount + loyaltyDiscount, Status = "posted",
                ReferenceType = "pos_transaction", Notes = $"بيع نقطة بيع - وردية {shift.ShiftNumber}",
                Items = lines.Select((l, i) => new InvoiceItemDto
                {
                    ItemId = l.ItemId, Quantity = l.Quantity, UnitPrice = products[l.ItemId].SellingPrice,
                    Discount = lineDiscounts[i], VatRate = products[l.ItemId].VatRate,
                    ExciseTaxRate = products[l.ItemId].ExciseTaxRate, ExciseRetailPrice = products[l.ItemId].ExciseRetailPrice,
                    ExciseStandardPrice = products[l.ItemId].ExciseStandardPrice,
                }).ToList(),
            };
            if (configured != null) configured.ApplyTo(invoiceDto);
            else ApplyPaymentToInvoice(invoiceDto, method, cash, card + mada + apple, total);
            if (online != null) invoiceDto.SettlementAccountCode = await OnlinePaymentService.SettlementAccountAsync(_db, token);
            var invoice = await _invoices.CreateAsync(invoiceDto, token);

            // 8) معاملة نقطة البيع (ما يخص الكاشير)
            var pointsEarned = customer == null ? 0 : (int)Math.Floor(total / PosPricing.SarPerEarnedPoint);
            var pos = new PosTransaction
            {
                InvoiceNumber = invoice.InvoiceNumber, ShiftId = shift.Id, CustomerId = customer?.Id,
                CustomerName = invoice.PartyName, CustomerPhone = invoice.PartyPhone, CustomerTaxNumber = vat,
                InvoiceType = standard ? "standard" : "simplified",
                SubtotalBeforeVat = DocumentPricing.Round(lines.Select((l, i) => l.Quantity * products[l.ItemId].SellingPrice).Sum()),
                CouponDiscount = couponDiscount, CouponCode = coupon?.Code, LoyaltyDiscount = loyaltyDiscount, LoyaltyPointsRedeemed = pointsUsed,
                TotalDiscount = lineDiscounts.Sum() + couponDiscount + loyaltyDiscount,
                ExciseTaxAmount = priced.ExciseTaxTotal, VatAmount = priced.VatTotal, GrandTotal = total, PaymentMethod = method,
                PaidCash = cash, PaidCard = card, PaidMada = mada, PaidApplePay = apple, ChangeAmount = change, PaymentsJson = configured?.Json,
                PointsEarned = pointsEarned, QrCodeBase64 = invoice.ZatcaQrCode ?? string.Empty,
                ZatcaStatus = invoice.ZatcaStatus, ZatcaUuid = invoice.Uuid.ToString(), InvoiceId = invoice.Id,
                Status = PosTransactionStatus.Completed,
            };
            for (var i = 0; i < lines.Count; i++)
            {
                var p = products[lines[i].ItemId]; var pl = priced.Lines[i];
                pos.Items.Add(new PosTransactionItem
                {
                    ItemId = p.Id, ItemCode = p.Sku, NameAr = p.NameAr, UnitAr = p.Unit, Quantity = lines[i].Quantity,
                    UnitPrice = p.SellingPrice, ExciseTaxRate = pl.ExciseTaxRate, ExciseRetailPrice = pl.ExciseRetailPrice,
                    ExciseStandardPrice = pl.ExciseStandardPrice, ExciseTaxBaseAmount = pl.ExciseTaxBaseAmount,
                    ExciseTaxAmount = pl.ExciseTaxAmount, VatRate = p.VatRate, VatAmount = pl.VatAmount,
                    Subtotal = DocumentPricing.Round(lines[i].Quantity * p.SellingPrice - lineDiscounts[i]),
                    Discount = lineDiscounts[i], TotalWithVat = pl.Total,
                });
            }
            _db.Add(pos);
            if (online != null) { online.ConsumedAt = DateTime.UtcNow; online.ReferenceId = pos.Id; online.ReferenceNumber = invoice.InvoiceNumber; }
            if (terminal != null) { terminal.ConsumedAt = DateTime.UtcNow; terminal.PosTransactionId = pos.Id; terminal.InvoiceNumber = invoice.InvoiceNumber; }

            // 9) الوردية والكوبون والولاء
            shift.TotalCashSales += cash; shift.TotalCardSales += card; shift.TotalMadaSales += mada; shift.TotalApplePaySales += apple;
            if (method == PosPaymentMethod.Credit) shift.TotalCreditSales += total;
            shift.TotalDiscount += pos.TotalDiscount; shift.TotalExciseTax += pos.ExciseTaxAmount;
            shift.TotalVat += pos.VatAmount; shift.TotalGross += total;
            if (coupon != null) coupon.UsageCount++;

            if (customer != null)
            {
                loyalty ??= NewLoyalty(customer);
                if (_db.Entry(loyalty).State == EntityState.Detached) _db.Add(loyalty);
                loyalty.PointsBalance += pointsEarned - pointsUsed;
                loyalty.TotalPointsEarned += pointsEarned; loyalty.TotalPointsRedeemed += pointsUsed;
                loyalty.PointsValueSar = loyalty.PointsBalance * PosPricing.PointValueSar;
                loyalty.Tier = PosPricing.TierFor(loyalty.TotalPointsEarned);
            }

            await _db.SaveChangesAsync(token);
            return ToDto(pos);
        }, ct);

    private sealed record CartPricing(
        List<CheckoutLineDto> Lines, Dictionary<Guid, Product> Products, List<decimal> OfferDiscounts, List<decimal> LineDiscounts,
        PosCoupon? Coupon, decimal CouponDiscount, string? CouponError,
        Customer? Customer, CustomerLoyalty? Loyalty, decimal LoyaltyDiscount, int PointsUsed, string? LoyaltyError,
        PricedDocument Priced);

    /// <summary>تسعير السلة من الكتالوج (مشترك بين الإتمام والعرض المسبق). strict=false: الكوبون/الولاء غير الصالح يُتجاهل مع رسالة.</summary>
    private async Task<CartPricing> PriceCartAsync(CheckoutRequestDto r, bool strict, PosShift shift, CancellationToken token)
    {
            // 1) السلة: دمج الأسطر المكررة والتحقق
            var lines = r.Items.Where(i => i.Quantity != 0 || i.ItemId != Guid.Empty)
                .GroupBy(i => i.ItemId).Select(g => new CheckoutLineDto
                {
                    ItemId = g.Key, Quantity = g.Sum(x => x.Quantity), ManualDiscount = g.Sum(x => x.ManualDiscount),
                    Note = string.Join(" | ", g.Select(x => x.Note).Where(n => !string.IsNullOrWhiteSpace(n))),
                }).ToList();
            if (lines.Count == 0) throw new ValidationFailedException(Messages.CartEmpty);
            if (lines.Any(l => l.Quantity <= 0 || l.ManualDiscount < 0)) throw new ValidationFailedException(Messages.QtyPositiveDiscountsNonNegative);
            // الخصم اليدوي صلاحية مستقلة على المبيعات (افتراضها القديم عمود الاعتماد: الأدوار الإدارية فقط)
            if (lines.Any(l => l.ManualDiscount > 0) && !await _permissions.HasPermissionAsync("sales", ScreenAction.Discount, ScreenAction.Approve, token))
                throw new ForbiddenException(Messages.ManualLineDiscountAdminOnly);

            var ids = lines.Select(l => l.ItemId).ToList();
            var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
            if (products.Count != ids.Count) throw new ValidationFailedException(Messages.ItemNotInCart);
            var categoryCodes = products.Values.Select(p => p.Category).Distinct().ToList();
            var categoryIds = await _db.Set<ProductCategory>().AsNoTracking().Where(c => categoryCodes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, c => c.Id, token);
            var allowedCategories = PosTerminalService.ReadIds(shift.AllowedCategoryIdsJson);
            if (allowedCategories.Count > 0 && products.Values.Any(p => !categoryIds.TryGetValue(p.Category, out var categoryId) || !allowedCategories.Contains(categoryId)))
                throw new ForbiddenException("تحتوي السلة على فئة غير مسموحة في نقطة البيع الحالية.");
            var offers = await _db.Set<PosOffer>().AsNoTracking().Where(o => o.IsActive).ToListAsync(token);

            // 2) الخصومات على مستوى السطر (عروض السرفر + خصم يدوي إداري)
            var offerDiscounts = new List<decimal>();
            var lineDiscounts = new List<decimal>();
            foreach (var l in lines)
            {
                var p = products[l.ItemId];
                var offer = PosPricing.BestOfferDiscount(offers, p.Id, categoryIds.TryGetValue(p.Category, out var cid) ? cid : null, l.Quantity, p.SellingPrice);
                var gross = l.Quantity * p.SellingPrice;
                if (l.ManualDiscount > gross - offer) throw new ValidationFailedException(string.Format(Messages.ItemDiscountExceedsValue, p.NameAr));
                offerDiscounts.Add(offer);
                lineDiscounts.Add(offer + l.ManualDiscount);
            }
            var netAfterLines = lines.Select((l, i) => l.Quantity * products[l.ItemId].SellingPrice - lineDiscounts[i]).Sum();

            // 3) الكوبون
            PosCoupon? coupon = null; decimal couponDiscount = 0; string? couponError = null;
            if (!string.IsNullOrWhiteSpace(r.CouponCode))
            {
                var code = r.CouponCode.Trim().ToUpperInvariant();
                coupon = await _db.Set<PosCoupon>().FirstOrDefaultAsync(c => c.Code == code, token);
                couponError = PosPricing.CouponRejectionReason(coupon, netAfterLines, SaudiTime.Now);
                if (couponError != null && strict) throw new ValidationFailedException(couponError);
                if (couponError != null) coupon = null;
                else couponDiscount = PosPricing.CouponDiscount(coupon!, netAfterLines);
            }

            // 4) الولاء
            Customer? customer = r.CustomerId.HasValue
                ? await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CustomerId, token) ?? throw new ValidationFailedException(Messages.CustomerNotFound)
                : null;
            CustomerLoyalty? loyalty = customer == null ? null : await _db.Set<CustomerLoyalty>().FirstOrDefaultAsync(l => l.CustomerId == customer.Id, token);
            decimal loyaltyDiscount = 0; var pointsUsed = 0; string? loyaltyError = null;
            if (r.LoyaltyPointsToRedeem > 0)
            {
                loyaltyError = loyalty == null ? Messages.NoLoyaltyBalanceForCustomer
                    : r.LoyaltyPointsToRedeem > loyalty.PointsBalance ? Messages.PointsExceedBalance : null;
                if (loyaltyError != null && strict) throw new ValidationFailedException(loyaltyError);
                if (loyaltyError == null)
                {
                    var maxDiscount = netAfterLines - couponDiscount;
                    loyaltyDiscount = DocumentPricing.Round(Math.Min(r.LoyaltyPointsToRedeem * PosPricing.PointValueSar, maxDiscount));
                    pointsUsed = (int)Math.Ceiling(loyaltyDiscount / PosPricing.PointValueSar);
                }
            }

            // 5) التسعير النهائي (نفس محرك الفواتير)
            var priced = DocumentPricing.Price(
                lines.Select((l, i) => new PricedLineInput(l.Quantity, products[l.ItemId].SellingPrice, lineDiscounts[i], products[l.ItemId].VatRate,
                    null, products[l.ItemId].ExciseTaxRate, products[l.ItemId].ExciseRetailPrice, products[l.ItemId].ExciseStandardPrice)).ToList(),
                couponDiscount + loyaltyDiscount);
            if (!shift.AllowDiscount && (lines.Any(l => l.ManualDiscount > 0) || couponDiscount > 0 || loyaltyDiscount > 0))
                throw new ForbiddenException("الخصومات غير مسموحة على نقطة البيع الحالية.");
            if (shift.MaxDiscountPercent.HasValue && priced.Subtotal > 0 && priced.DiscountTotal * 100m / priced.Subtotal > shift.MaxDiscountPercent.Value)
                throw new ForbiddenException($"تجاوز الخصم الحد المسموح لهذه النقطة ({shift.MaxDiscountPercent.Value:0.##}%).");
            return new CartPricing(lines, products, offerDiscounts, lineDiscounts, coupon, couponDiscount, couponError,
                customer, loyalty, loyaltyDiscount, pointsUsed, loyaltyError, priced);
    }

    /// <summary>تسعير السلة من السرفر للعرض على شاشة الكاشير قبل الدفع — بلا أي أثر.</summary>
    public async Task<PosQuoteDto> QuoteAsync(CheckoutRequestDto r, CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var shift = await _db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, ct)
            ?? throw new ConflictException(Messages.OpenShiftBeforeSale);
        var cart = await PriceCartAsync(r, strict: false, shift, ct);
        var p = cart.Priced;
        return new PosQuoteDto
        {
            Lines = cart.Lines.Select((l, i) =>
            {
                var prod = cart.Products[l.ItemId]; var pl = p.Lines[i];
                return new PosQuoteLineDto
                {
                    ItemId = prod.Id, Sku = prod.Sku, NameAr = prod.NameAr, NameEn = prod.NameEn, Unit = prod.Unit,
                    Quantity = l.Quantity, UnitPrice = prod.SellingPrice, VatRate = prod.VatRate,
                    OfferDiscount = cart.OfferDiscounts[i], ManualDiscount = l.ManualDiscount,
                    Net = pl.Net, ExciseTaxBaseAmount = pl.ExciseTaxBaseAmount, ExciseTaxAmount = pl.ExciseTaxAmount,
                    VatAmount = pl.VatAmount, Total = pl.Total, StockOnHand = prod.CurrentStock,
                };
            }).ToList(),
            GrossTotal = p.Subtotal, LineDiscounts = cart.LineDiscounts.Sum(),
            CouponDiscount = cart.CouponDiscount, CouponCode = cart.Coupon?.Code, CouponError = cart.CouponError,
            LoyaltyDiscount = cart.LoyaltyDiscount, LoyaltyPointsRedeemed = cart.PointsUsed, LoyaltyError = cart.LoyaltyError,
            LoyaltyPointsBalance = cart.Loyalty?.PointsBalance ?? 0,
            TotalDiscount = cart.LineDiscounts.Sum() + cart.CouponDiscount + cart.LoyaltyDiscount,
            NetBeforeVat = p.NetTotal, ExciseTaxTotal = p.ExciseTaxTotal, VatTotal = p.VatTotal, GrandTotal = p.GrandTotal,
            PointsToEarn = cart.Customer == null ? 0 : (int)Math.Floor(p.GrandTotal / PosPricing.SarPerEarnedPoint),
        };
    }

    private static CustomerLoyalty NewLoyalty(Customer c) => new() { CustomerId = c.Id, CustomerName = c.NameAr, Phone = c.Phone };

    /// <summary>يوزّع المدفوعات ويحسب الباقي. الباقي يُرد من النقد فقط.</summary>
    private static (decimal Cash, decimal Card, decimal Mada, decimal Apple, decimal Change) ResolvePayment(CheckoutRequestDto r, decimal total, Customer? customer)
    {
        if (r.PaidCash < 0 || r.PaidCard < 0 || r.PaidMada < 0 || r.PaidApplePay < 0) throw new ValidationFailedException(Messages.PaidAmountsCannotBeNegative);
        switch (r.PaymentMethod)
        {
            case PosPaymentMethod.Cash:
                if (r.PaidCash < total) throw new ValidationFailedException(Messages.CashPaidBelowTotal);
                return (total, 0, 0, 0, DocumentPricing.Round(r.PaidCash - total));
            case PosPaymentMethod.Card: return (0, total, 0, 0, 0);
            case PosPaymentMethod.Mada: return (0, 0, total, 0, 0);
            case PosPaymentMethod.ApplePay: return (0, 0, 0, total, 0);
            case PosPaymentMethod.Credit:
                if (customer == null) throw new ValidationFailedException(Messages.CreditSaleRequiresCustomer);
                return (0, 0, 0, 0, 0);
            case PosPaymentMethod.Split:
                var paid = r.PaidCash + r.PaidCard + r.PaidMada + r.PaidApplePay;
                if (paid < total) throw new ValidationFailedException(Messages.PaymentsBelowTotal);
                var over = paid - total;
                if (over > r.PaidCash) throw new ValidationFailedException(Messages.ChangeFromCashOnly);
                return (DocumentPricing.Round(r.PaidCash - over), r.PaidCard, r.PaidMada, r.PaidApplePay, DocumentPricing.Round(over));
            default: throw new ValidationFailedException(Messages.InvalidPaymentMethod);
        }
    }

    /// <summary>الدفع بطرق جدول طرق الدفع: الأرصدة لدرج الوردية، ونوع العملية، وأسطر دفع الفاتورة برموزها، وما يُحفظ للإيصال.</summary>
    private sealed record ConfiguredPayment(
        (decimal Cash, decimal Card, decimal Mada, decimal Apple, decimal Change) Buckets, PosPaymentMethod Method, List<InvoicePaymentSplitDto> Splits, string Json)
    {
        public void ApplyTo(CreateInvoiceDto dto)
        {
            dto.PaymentMethod = Splits[0].Method;
            if (Splits.Count == 1) { dto.PaymentMethodCode = Splits[0].PaymentMethodCode; return; }
            dto.IsSplitPayment = true;
            dto.PaymentSplits = Splits;
        }
    }

    /// <summary>
    /// يعتمد طرق الدفع المختارة (فعّالة) ويوزّعها: النقد لدرج الوردية والباقي يُرد منه فقط، والبطاقة/البنك/الشيك لمبيعات البطاقات،
    /// والآجل كامل المبلغ لعميل مسجّل ولا يُخلط بغيره (ليبقى الإلغاء وتسوية الوردية صحيحين).
    /// </summary>
    private async Task<ConfiguredPayment> ResolveConfiguredPaymentAsync(List<PosPaymentLineDto> lines, decimal total, Customer? customer, CancellationToken ct)
    {
        if (lines.Any(l => l.Amount <= 0)) throw new ValidationFailedException(Messages.SplitPaymentsNeedPositiveAmounts);
        var chosen = new List<(PosPaymentLineDto Line, PaymentMethodItem Method)>();
        foreach (var l in lines)
            chosen.Add((l, await PaymentMethodRules.RequireAsync(_db, l.PaymentMethodCode, ct) ?? throw new ValidationFailedException(Messages.InvalidPaymentMethod)));
        foreach (var (l, m) in chosen) { l.PaymentMethodCode = m.Code; l.NameAr = m.NameAr; l.NameEn = m.NameEn; l.Type = m.Type; }
        var json = System.Text.Json.JsonSerializer.Serialize(lines);

        if (chosen.Any(x => x.Method.Type == "credit"))
        {
            if (chosen.Count > 1) throw new ValidationFailedException(Messages.PosCreditNotMixed);
            if (customer == null) throw new ValidationFailedException(Messages.CreditSaleRequiresCustomer);
            var split = new InvoicePaymentSplitDto { Method = PaymentMethod.Credit, PaymentMethodCode = chosen[0].Method.Code, Amount = total };
            return new((0, 0, 0, 0, 0), PosPaymentMethod.Credit, new() { split }, json);
        }

        var paid = chosen.Sum(x => x.Line.Amount);
        var cashIn = chosen.Where(x => x.Method.Type == "cash").Sum(x => x.Line.Amount);
        if (paid < total) throw new ValidationFailedException(Messages.PaymentsBelowTotal);
        var change = DocumentPricing.Round(paid - total);
        if (change > cashIn) throw new ValidationFailedException(Messages.ChangeFromCashOnly);

        // الباقي يُخصم من أسطر النقد بالترتيب، فيُرحَّل على كل طريقة ما بقي فيها فعلاً
        var splits = new List<InvoicePaymentSplitDto>();
        var changeLeft = change;
        foreach (var (l, m) in chosen)
        {
            var amount = l.Amount;
            if (m.Type == "cash" && changeLeft > 0) { var cut = Math.Min(changeLeft, amount); amount -= cut; changeLeft -= cut; }
            if (amount > 0) splits.Add(new InvoicePaymentSplitDto { Method = PaymentMethodRules.MethodOf(m.Type), PaymentMethodCode = m.Code, Amount = DocumentPricing.Round(amount), Reference = l.Reference });
        }
        var cash = DocumentPricing.Round(cashIn - change);
        var nonCash = DocumentPricing.Round(paid - cashIn);
        var method = cash > 0 && nonCash > 0 ? PosPaymentMethod.Split : cash > 0 ? PosPaymentMethod.Cash : PosPaymentMethod.Card;
        return new((cash, nonCash, 0, 0, change), method, splits, json);
    }

    /// <summary>
    /// طريقة مفعّل لها جهاز الدفع لا تُقبل إلا بدفع مُوافَق عليه من الجهاز، غير مستخدم، من الوردية نفسها، وبكامل الإجمالي
    /// كسطر دفع وحيد. رقم مرجع العملية (RRN) يُحفظ مرجعاً لسطر الدفع في الفاتورة.
    /// </summary>
    private async Task<CardTerminalPayment?> RequireTerminalPaymentAsync(CheckoutRequestDto r, PosShift shift, decimal total, CancellationToken ct)
    {
        var codes = r.Payments.Select(l => l.PaymentMethodCode?.Trim()).Where(c => !string.IsNullOrEmpty(c)).ToList();
        var usesTerminal = codes.Count > 0 && await _db.Set<PaymentMethodItem>().AnyAsync(m => m.UseCardTerminal && codes.Contains(m.Code), ct);
        if (!r.CardTerminalPaymentId.HasValue)
        {
            if (usesTerminal) throw new ValidationFailedException(Messages.CardTerminalPaymentRequired);
            return null;
        }
        if (r.OnlinePaymentId.HasValue || r.Payments.Count != 1) throw new ValidationFailedException(Messages.CardTerminalSingleLineOnly);

        var p = await _db.Set<CardTerminalPayment>().FirstOrDefaultAsync(x => x.Id == r.CardTerminalPaymentId, ct)
            ?? throw new NotFoundException(Messages.CardTerminalPaymentNotFound);
        if (p.Status != CardTerminalPaymentStatus.Approved) throw new ConflictException(Messages.CardTerminalPaymentNotApproved);
        if (p.ConsumedAt != null) throw new ConflictException(Messages.PaymentAlreadyUsed);
        if (p.ShiftId != shift.Id) throw new ConflictException(Messages.CardTerminalPaymentOtherShift);
        var line = r.Payments[0];
        if (!string.Equals(line.PaymentMethodCode?.Trim(), p.PaymentMethodCode, StringComparison.Ordinal))
            throw new ValidationFailedException(Messages.CardTerminalPaymentMethodMismatch);
        if (p.Amount != total || line.Amount != total)
            throw new ConflictException(string.Format(Messages.OnlinePaymentAmountMismatch, p.Amount, total));
        line.Reference = p.Rrn;
        return p;
    }

    /// <summary>عملية نقطة البيع للواجهة، مع طرق الدفع المحفوظة.</summary>
    private static PosTransactionDto ToDto(PosTransaction t)
    {
        var dto = Mapper.Map<PosTransactionDto>(t);
        if (!string.IsNullOrWhiteSpace(t.PaymentsJson))
            try { dto.Payments = System.Text.Json.JsonSerializer.Deserialize<List<PosPaymentLineDto>>(t.PaymentsJson) ?? new(); } catch { /* سجل تالف: يُعرض بالأرصدة */ }
        return dto;
    }

    /// <summary>«مقسّم» سُدِّد كله بطريقة واحدة هو دفع بتلك الطريقة: يُحفظ ويُرحَّل ويظهر في التقارير كذلك.</summary>
    private static PosPaymentMethod EffectiveMethod(PosPaymentMethod requested, decimal cash, decimal card, decimal mada, decimal apple)
    {
        if (requested != PosPaymentMethod.Split) return requested;
        var used = new[] { (PosPaymentMethod.Cash, cash), (PosPaymentMethod.Card, card), (PosPaymentMethod.Mada, mada), (PosPaymentMethod.ApplePay, apple) }
            .Where(t => t.Item2 > 0).ToList();
        return used.Count == 1 ? used[0].Item1 : PosPaymentMethod.Split;
    }

    private static void ApplyPaymentToInvoice(CreateInvoiceDto dto, PosPaymentMethod method, decimal cash, decimal nonCash, decimal total)
    {
        switch (method)
        {
            case PosPaymentMethod.Cash: dto.PaymentMethod = PaymentMethod.Cash; break;
            case PosPaymentMethod.Credit: dto.PaymentMethod = PaymentMethod.Credit; break;
            case PosPaymentMethod.Split:
                dto.PaymentMethod = cash >= nonCash ? PaymentMethod.Cash : PaymentMethod.BankCard;
                dto.IsSplitPayment = true;
                if (cash > 0) dto.PaymentSplits.Add(new InvoicePaymentSplitDto { Method = PaymentMethod.Cash, Amount = cash });
                if (nonCash > 0) dto.PaymentSplits.Add(new InvoicePaymentSplitDto { Method = PaymentMethod.BankCard, Amount = nonCash });
                break;
            default: dto.PaymentMethod = PaymentMethod.BankCard; break; // بطاقة/مدى/Apple Pay: تسوية بنكية
        }
    }

    /// <summary>
    /// إلغاء عملية بيع: الفاتورة الضريبية الصادرة لا تُحذف ولا تُعدَّل، فتُلغى بإشعار دائن كامل (قيد ومخزون معكوسان)
    /// يردّ المبلغ بطرق الدفع نفسها، ويُسحب أثرها من الوردية والكوبون والولاء، وتبقى العملية في السجل «ملغاة».
    /// </summary>
    public Task<PosTransactionDto> VoidAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var t = await _db.Set<PosTransaction>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.TransactionNotFound);
            var shift = await _db.Set<PosShift>().FirstAsync(s => s.Id == t.ShiftId, token);
            PosShiftRules.EnsureCanCorrect(shift, _user);
            if (t.Status == PosTransactionStatus.Voided) throw new ConflictException(Messages.TransactionAlreadyVoided);
            if (await _db.Set<PosSalesReturn>().AnyAsync(x => x.OriginalTransactionId == id, token))
                throw new ConflictException(Messages.TransactionHasReturns);

            shift.TotalCashSales -= t.PaidCash; shift.TotalCardSales -= t.PaidCard; shift.TotalMadaSales -= t.PaidMada; shift.TotalApplePaySales -= t.PaidApplePay;
            if (t.PaymentMethod == PosPaymentMethod.Credit) shift.TotalCreditSales -= t.GrandTotal;
            shift.TotalDiscount -= t.TotalDiscount; shift.TotalExciseTax -= t.ExciseTaxAmount;
            shift.TotalVat -= t.VatAmount; shift.TotalGross -= t.GrandTotal;
            await PosCashDrawer.SyncVarianceAsync(_db, _posting, shift, token);

            if (!string.IsNullOrWhiteSpace(t.CouponCode))
            {
                var coupon = await _db.Set<PosCoupon>().FirstOrDefaultAsync(c => c.Code == t.CouponCode, token);
                if (coupon != null && coupon.UsageCount > 0) coupon.UsageCount--;
            }
            if (t.CustomerId.HasValue)
            {
                var loyalty = await _db.Set<CustomerLoyalty>().FirstOrDefaultAsync(l => l.CustomerId == t.CustomerId, token);
                if (loyalty != null)
                {
                    loyalty.PointsBalance = Math.Max(0, loyalty.PointsBalance - t.PointsEarned + t.LoyaltyPointsRedeemed);
                    loyalty.TotalPointsEarned = Math.Max(0, loyalty.TotalPointsEarned - t.PointsEarned);
                    loyalty.TotalPointsRedeemed = Math.Max(0, loyalty.TotalPointsRedeemed - t.LoyaltyPointsRedeemed);
                    loyalty.PointsValueSar = loyalty.PointsBalance * PosPricing.PointValueSar;
                    loyalty.Tier = PosPricing.TierFor(loyalty.TotalPointsEarned);
                }
            }

            t.Status = PosTransactionStatus.Voided;
            await _db.SaveChangesAsync(token);
            if (t.InvoiceId.HasValue)
                await _returns.CreateFromInvoiceAsync(new CreateReturnInvoiceRequestDto
                {
                    OriginalInvoiceId = t.InvoiceId.Value, ReturnReason = string.Format(Messages.PosVoidReturnReason, t.InvoiceNumber),
                }, token);
            return ToDto(t);
        }, ct);

    public async Task<PagedResult<PosTransactionDto>> ListAsync(Guid? shiftId, PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<PosTransaction>().AsNoTracking().AsQueryable();
        if (shiftId.HasValue) q = q.Where(t => t.ShiftId == shiftId);
        if (p.StartDate.HasValue) q = q.Where(t => t.CreatedAt >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(t => t.CreatedAt <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(x => x.InvoiceNumber.Contains(t) || x.CustomerName.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(t => t.Items).OrderByDescending(t => t.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<PosTransactionDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<PosTransactionDto> GetAsync(Guid id, CancellationToken ct = default)
        => ToDto(await _db.Set<PosTransaction>().AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(Messages.TransactionNotFound));

    public async Task<PosTransactionDto> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default)
        => ToDto(await _db.Set<PosTransaction>().AsNoTracking().Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.InvoiceNumber == invoiceNumber, ct) ?? throw new NotFoundException(Messages.NoTransactionWithNumber));

    public async Task<ZatcaSubmitResultDto> SubmitToZatcaAsync(Guid id, CancellationToken ct = default)
    {
        var tx = await _db.Set<PosTransaction>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException(Messages.TransactionNotFound);
        return await _invoices.SubmitToZatcaAsync(tx.InvoiceId ?? throw new ConflictException(Messages.NoLinkedInvoice), ct);
    }
}
