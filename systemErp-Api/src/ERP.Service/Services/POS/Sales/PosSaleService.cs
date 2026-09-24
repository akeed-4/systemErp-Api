using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

/// <summary>
/// بيع نقطة البيع. لا يبني قيوداً ولا حركات مخزون بنفسه: يحوّل السلة إلى فاتورة مبيعات عبر IInvoiceService
/// (مخزون + قيد + QR)، ثم يحفظ ما هو خاص بالكاشير (الوردية، الكوبون، الولاء، الباقي) — كل ذلك في معاملة واحدة.
/// </summary>
public class PosSaleService : IPosSaleService
{
    private static readonly HashSet<string> ManualDiscountRoles = new() { "owner", "admin", "general_manager", "chief_accountant" };

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IInvoiceService _invoices;
    private readonly ITransactionRunner _tx;

    public PosSaleService(ErpDbContext db, ICurrentUser user, IInvoiceService invoices, ITransactionRunner tx)
    {
        _db = db; _user = user; _invoices = invoices; _tx = tx;
    }

    public Task<PosTransactionDto> CheckoutAsync(CheckoutRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token)
                ?? throw new ConflictException("افتح وردية أولاً قبل البيع.");
            var settings = await _db.Set<PosInvoiceSettings>().AsNoTracking().FirstOrDefaultAsync(token) ?? new PosInvoiceSettings();

            // 1) السلة: دمج الأسطر المكررة والتحقق
            var lines = r.Items.Where(i => i.Quantity != 0 || i.ItemId != Guid.Empty)
                .GroupBy(i => i.ItemId).Select(g => new CheckoutLineDto
                {
                    ItemId = g.Key, Quantity = g.Sum(x => x.Quantity), ManualDiscount = g.Sum(x => x.ManualDiscount),
                    Note = string.Join(" | ", g.Select(x => x.Note).Where(n => !string.IsNullOrWhiteSpace(n))),
                }).ToList();
            if (lines.Count == 0) throw new ValidationFailedException("السلة فارغة.");
            if (lines.Any(l => l.Quantity <= 0 || l.ManualDiscount < 0)) throw new ValidationFailedException("الكميات موجبة والخصومات غير سالبة.");
            if (lines.Any(l => l.ManualDiscount > 0) && !ManualDiscountRoles.Contains(_user.RoleId ?? string.Empty))
                throw new ForbiddenException("الخصم اليدوي على السطر للأدوار الإدارية فقط.");

            var ids = lines.Select(l => l.ItemId).ToList();
            var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
            if (products.Count != ids.Count) throw new ValidationFailedException("صنف غير موجود في السلة.");
            var categoryCodes = products.Values.Select(p => p.Category).Distinct().ToList();
            var categoryIds = await _db.Set<ProductCategory>().AsNoTracking().Where(c => categoryCodes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, c => c.Id, token);
            var offers = await _db.Set<PosOffer>().AsNoTracking().Where(o => o.IsActive).ToListAsync(token);

            // 2) الخصومات على مستوى السطر (عروض الخادم + خصم يدوي إداري)
            var lineDiscounts = new List<decimal>();
            foreach (var l in lines)
            {
                var p = products[l.ItemId];
                var offer = PosPricing.BestOfferDiscount(offers, p.Id, categoryIds.TryGetValue(p.Category, out var cid) ? cid : null, l.Quantity, p.SellingPrice);
                var gross = l.Quantity * p.SellingPrice;
                if (l.ManualDiscount > gross - offer) throw new ValidationFailedException($"الخصم على الصنف {p.NameAr} يتجاوز قيمته.");
                lineDiscounts.Add(offer + l.ManualDiscount);
            }
            var netAfterLines = lines.Select((l, i) => l.Quantity * products[l.ItemId].SellingPrice - lineDiscounts[i]).Sum();

            // 3) الكوبون
            PosCoupon? coupon = null; decimal couponDiscount = 0;
            if (!string.IsNullOrWhiteSpace(r.CouponCode))
            {
                var code = r.CouponCode.Trim().ToUpperInvariant();
                coupon = await _db.Set<PosCoupon>().FirstOrDefaultAsync(c => c.Code == code, token);
                var reason = PosPricing.CouponRejectionReason(coupon, netAfterLines, DateTime.UtcNow);
                if (reason != null) throw new ValidationFailedException(reason);
                couponDiscount = PosPricing.CouponDiscount(coupon!, netAfterLines);
            }

            // 4) الولاء
            Customer? customer = r.CustomerId.HasValue
                ? await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CustomerId, token) ?? throw new ValidationFailedException("العميل غير موجود.")
                : null;
            CustomerLoyalty? loyalty = customer == null ? null : await _db.Set<CustomerLoyalty>().FirstOrDefaultAsync(l => l.CustomerId == customer.Id, token);
            decimal loyaltyDiscount = 0; var pointsUsed = 0;
            if (r.LoyaltyPointsToRedeem > 0)
            {
                if (loyalty == null) throw new ValidationFailedException("لا يوجد رصيد ولاء لهذا العميل.");
                if (r.LoyaltyPointsToRedeem > loyalty.PointsBalance) throw new ValidationFailedException("النقاط المطلوبة أكبر من الرصيد.");
                var maxDiscount = netAfterLines - couponDiscount;
                loyaltyDiscount = DocumentPricing.Round(Math.Min(r.LoyaltyPointsToRedeem * PosPricing.PointValueSar, maxDiscount));
                pointsUsed = (int)Math.Ceiling(loyaltyDiscount / PosPricing.PointValueSar);
            }

            // 5) التسعير النهائي (نفس محرك الفواتير)
            var priced = DocumentPricing.Price(
                lines.Select((l, i) => new PricedLineInput(l.Quantity, products[l.ItemId].SellingPrice, lineDiscounts[i], products[l.ItemId].VatRate)).ToList(),
                couponDiscount + loyaltyDiscount);
            var total = priced.GrandTotal;

            // 6) الدفع
            var (cash, card, mada, apple, change) = ResolvePayment(r, total, customer);

            // 7) الفاتورة (مخزون + قيد + QR) عبر محرك الفوترة المركزي
            var standard = string.Equals(r.InvoiceType ?? settings.DefaultInvoiceType, "standard", StringComparison.OrdinalIgnoreCase);
            var vat = string.IsNullOrWhiteSpace(r.CustomerTaxNumber) ? customer?.VatNumber : r.CustomerTaxNumber;
            if (standard && string.IsNullOrWhiteSpace(vat)) throw new ValidationFailedException("الفاتورة القياسية تتطلب الرقم الضريبي للعميل.");

            var invoiceDto = new CreateInvoiceDto
            {
                Kind = InvoiceKind.Sales, InvoiceType = standard ? InvoiceType.TaxInvoice : InvoiceType.Simplified,
                PartyId = customer?.Id, PartyName = r.CustomerName ?? customer?.NameAr ?? "عميل نقدي POS",
                PartyVatNumber = vat, PartyPhone = r.CustomerPhone ?? customer?.Phone,
                InvoiceDiscount = couponDiscount + loyaltyDiscount, Status = "posted",
                ReferenceType = "pos_transaction", Notes = $"بيع نقطة بيع - وردية {shift.ShiftNumber}",
                Items = lines.Select((l, i) => new InvoiceItemDto
                {
                    ItemId = l.ItemId, Quantity = l.Quantity, UnitPrice = products[l.ItemId].SellingPrice,
                    Discount = lineDiscounts[i], VatRate = products[l.ItemId].VatRate,
                }).ToList(),
            };
            ApplyPaymentToInvoice(invoiceDto, r.PaymentMethod, cash, card + mada + apple, total);
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
                VatAmount = priced.VatTotal, GrandTotal = total, PaymentMethod = r.PaymentMethod,
                PaidCash = cash, PaidCard = card, PaidMada = mada, PaidApplePay = apple, ChangeAmount = change,
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
                    UnitPrice = p.SellingPrice, VatRate = p.VatRate, VatAmount = pl.VatAmount,
                    Subtotal = DocumentPricing.Round(lines[i].Quantity * p.SellingPrice - lineDiscounts[i]),
                    Discount = lineDiscounts[i], TotalWithVat = pl.Total,
                });
            }
            _db.Add(pos);

            // 9) الوردية والكوبون والولاء
            shift.TotalCashSales += cash; shift.TotalCardSales += card; shift.TotalMadaSales += mada; shift.TotalApplePaySales += apple;
            if (r.PaymentMethod == PosPaymentMethod.Credit) shift.TotalCreditSales += total;
            shift.TotalDiscount += pos.TotalDiscount; shift.TotalVat += pos.VatAmount; shift.TotalGross += total;
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
            return Mapper.Map<PosTransactionDto>(pos);
        }, ct);

    private static CustomerLoyalty NewLoyalty(Customer c) => new() { CustomerId = c.Id, CustomerName = c.NameAr, Phone = c.Phone };

    /// <summary>يوزّع المدفوعات ويحسب الباقي. الباقي يُرد من النقد فقط.</summary>
    private static (decimal Cash, decimal Card, decimal Mada, decimal Apple, decimal Change) ResolvePayment(CheckoutRequestDto r, decimal total, Customer? customer)
    {
        if (r.PaidCash < 0 || r.PaidCard < 0 || r.PaidMada < 0 || r.PaidApplePay < 0) throw new ValidationFailedException("المبالغ المدفوعة لا تكون سالبة.");
        switch (r.PaymentMethod)
        {
            case PosPaymentMethod.Cash:
                if (r.PaidCash < total) throw new ValidationFailedException("المبلغ النقدي المدفوع أقل من الإجمالي.");
                return (total, 0, 0, 0, DocumentPricing.Round(r.PaidCash - total));
            case PosPaymentMethod.Card: return (0, total, 0, 0, 0);
            case PosPaymentMethod.Mada: return (0, 0, total, 0, 0);
            case PosPaymentMethod.ApplePay: return (0, 0, 0, total, 0);
            case PosPaymentMethod.Credit:
                if (customer == null) throw new ValidationFailedException("البيع الآجل يتطلب تحديد العميل.");
                return (0, 0, 0, 0, 0);
            case PosPaymentMethod.Split:
                var paid = r.PaidCash + r.PaidCard + r.PaidMada + r.PaidApplePay;
                if (paid < total) throw new ValidationFailedException("مجموع المدفوعات أقل من الإجمالي.");
                var over = paid - total;
                if (over > r.PaidCash) throw new ValidationFailedException("الباقي يُرد من النقد فقط.");
                return (DocumentPricing.Round(r.PaidCash - over), r.PaidCard, r.PaidMada, r.PaidApplePay, DocumentPricing.Round(over));
            default: throw new ValidationFailedException("طريقة دفع غير صالحة.");
        }
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

    public Task<PosTransactionDto> UpdateAsync(Guid id, UpdatePosTransactionRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(r.CustomerName)) throw new ValidationFailedException("اسم العميل مطلوب.");
            var t = await _db.Set<PosTransaction>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("المعاملة غير موجودة");
            if (t.Status == PosTransactionStatus.Voided) throw new ConflictException("المعاملة ملغاة ولا تُعدَّل.");
            var shift = await _db.Set<PosShift>().FirstAsync(s => s.Id == t.ShiftId, token);
            PosShiftRules.EnsureCanCorrect(shift, _user);
            var vat = string.IsNullOrWhiteSpace(r.CustomerTaxNumber) ? null : r.CustomerTaxNumber.Trim();
            if (t.InvoiceType == "standard" && vat == null) throw new ValidationFailedException("الفاتورة القياسية تتطلب الرقم الضريبي للعميل.");

            t.CustomerName = r.CustomerName.Trim(); t.CustomerPhone = r.CustomerPhone; t.CustomerTaxNumber = vat;
            if (t.InvoiceId.HasValue)
            {
                var inv = await _db.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == t.InvoiceId, token);
                if (inv != null) { inv.PartyName = t.CustomerName; inv.PartyPhone = t.CustomerPhone; inv.PartyVatNumber = vat; }
            }
            await _db.SaveChangesAsync(token);
            return Mapper.Map<PosTransactionDto>(t);
        }, ct);

    public Task<PosTransactionDto> VoidAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token => Mapper.Map<PosTransactionDto>(await ReverseAsync(id, remove: false, token)), ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token => { await ReverseAsync(id, remove: true, token); return 0; }, ct);

    /// <summary>يعكس أثر المعاملة كلياً (فاتورة/قيد/مخزون/وردية/كوبون/ولاء) ثم يُبقيها ملغاة أو يحذفها.</summary>
    private async Task<PosTransaction> ReverseAsync(Guid id, bool remove, CancellationToken token)
    {
        var t = await _db.Set<PosTransaction>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("المعاملة غير موجودة");
        var shift = await _db.Set<PosShift>().FirstAsync(s => s.Id == t.ShiftId, token);
        PosShiftRules.EnsureCanCorrect(shift, _user);
        if (t.Status == PosTransactionStatus.Voided && !remove) throw new ConflictException("المعاملة ملغاة مسبقاً.");
        if (await _db.Set<PosSalesReturn>().AnyAsync(x => x.OriginalTransactionId == id, token))
            throw new ConflictException("للمعاملة مرتجعات؛ احذفها أولاً.");

        if (t.Status != PosTransactionStatus.Voided)
        {
            shift.TotalCashSales -= t.PaidCash; shift.TotalCardSales -= t.PaidCard; shift.TotalMadaSales -= t.PaidMada; shift.TotalApplePaySales -= t.PaidApplePay;
            if (t.PaymentMethod == PosPaymentMethod.Credit) shift.TotalCreditSales -= t.GrandTotal;
            shift.TotalDiscount -= t.TotalDiscount; shift.TotalVat -= t.VatAmount; shift.TotalGross -= t.GrandTotal;
            PosShiftRules.RecomputeVariance(shift);

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
        }

        var invoiceId = t.InvoiceId;
        t.Status = PosTransactionStatus.Voided; t.InvoiceId = null;
        if (remove) _db.Remove(t);
        await _db.SaveChangesAsync(token);
        if (invoiceId.HasValue) await _invoices.DeleteSourceInvoiceAsync(invoiceId.Value, token);
        return t;
    }

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
            Items = items.Select(Mapper.Map<PosTransactionDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<PosTransactionDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<PosTransactionDto>(await _db.Set<PosTransaction>().AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("المعاملة غير موجودة"));

    public async Task<PosTransactionDto> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default)
        => Mapper.Map<PosTransactionDto>(await _db.Set<PosTransaction>().AsNoTracking().Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.InvoiceNumber == invoiceNumber, ct) ?? throw new NotFoundException("لا توجد معاملة بهذا الرقم"));

    public async Task<ZatcaSubmitResultDto> SubmitToZatcaAsync(Guid id, CancellationToken ct = default)
    {
        var tx = await _db.Set<PosTransaction>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("المعاملة غير موجودة");
        return await _invoices.SubmitToZatcaAsync(tx.InvoiceId ?? throw new ConflictException("لا توجد فاتورة مرتبطة."), ct);
    }
}
