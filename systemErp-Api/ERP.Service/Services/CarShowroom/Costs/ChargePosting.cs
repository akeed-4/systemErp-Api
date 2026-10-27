using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// القيود المحاسبية لمصاريف السيارات. كل الحسابات من إعدادات المعرض (CarShowroomSettings) أو من نوع المصروف؛ لا حساب ثابت هنا.
/// المدين حسب المعالجة: إضافة للتكلفة ← مخزون السيارات أو «سيارات في الطريق»، مصروف ← حساب النوع، على العميل ← مدفوعات نيابة عن العملاء
/// (أو تكلفة الإضافات لسطر له سعر بيع). الدائن: المورد، أو رصيد تم، أو حساب الدفع، وإلا المستحقات. الضريبة المستردة ← ضريبة المدخلات.
/// القيد عبر المحرك المركزي داخل معاملة المتصل.
/// </summary>
internal static class ChargePosting
{
    public const string LineSource = "car_charge_line";
    public const string VoucherSource = "car_landed_cost_voucher";
    public const string ReceiptSource = "car_vehicle_receipt";

    /// <summary>حسابات المعرض الافتراضية تُنشأ للمنشأة إن نقصت (منشآت هُيّئت قبل إضافتها للشجرة).</summary>
    public static Task EnsureAccountsAsync(ErpDbContext db, CarShowroomSettings s, CancellationToken ct)
        => DefaultAccounts.EnsureAsync(db, ct, s.VehicleInventoryAccount, s.VehiclesInTransitAccount, s.CarSalesRevenueAccount, s.CarCogsAccount,
            s.AccruedChargesAccount, s.TammWalletAccount, s.CustomerRechargeAccount, s.AddOnRevenueAccount, s.AddOnCostAccount, s.VehicleExpenseAccount,
            s.CustomerDepositsAccount, s.PrepaidExpensesAccount, s.PurchaseCancellationLossAccount, s.SupplierClaimsAccount, s.ForfeitedDepositsAccount,
            s.ImportCostsClearingAccount, DefaultAccounts.InputVat);

    /// <summary>أسطر المدين لمصروف واحد. <paramref name="mark"/> يسجّل على كل حصة هل قُيِّدت على «في الطريق».</summary>
    private static async Task<List<PostingLine>> DebitsAsync(ErpDbContext db, AdditionalChargeLine line, ExpenseType? type, CarShowroomSettings s, bool mark, CancellationToken ct)
    {
        var debits = new List<PostingLine>();
        var baseVat = DocumentPricing.Round(line.VatAmount * line.ExchangeRate);
        // ضريبة المدخلات المستردة: ضريبة السطر، أو مبلغه كله إن كان النوع ضريبة (مثل ضريبة الاستيراد)
        var inputVat = line.IsInputTax ? (line.IsVatRecoverable ? line.BaseAmount : 0) : (line.IsVatRecoverable ? baseVat : 0);

        if (line.CostAmount > 0)
            switch (line.Treatment)
            {
                case ChargeTreatment.Capitalize:
                    var vehicleIds = line.Allocations.Select(a => a.VehicleId).ToList();
                    var statuses = await db.Set<Vehicle>().Where(v => vehicleIds.Contains(v.Id)).Select(v => new { v.Id, v.Status }).ToDictionaryAsync(v => v.Id, v => v.Status, ct);
                    decimal transit = 0, stock = 0, cogs = 0;
                    foreach (var allocation in line.Allocations)
                    {
                        var status = statuses.GetValueOrDefault(allocation.VehicleId);
                        var onTheWay = status == VehicleStatus.InTransit;
                        var sold = status == VehicleStatus.Sold;
                        if (mark) { allocation.PostedInTransit = onTheWay; allocation.ReclassedToInventory = false; allocation.PostedToCogs = sold; }
                        if (sold) cogs += allocation.Amount; else if (onTheWay) transit += allocation.Amount; else stock += allocation.Amount;
                    }
                    if (stock > 0) debits.Add(new(s.VehicleInventoryAccount, stock, 0));
                    if (transit > 0) debits.Add(new(s.VehiclesInTransitAccount, transit, 0));
                    // مصروف فعلي وصل بعد بيع السيارة: على تكلفة المبيعات مباشرة لا على المخزون
                    if (cogs > 0) debits.Add(new(s.CarCogsAccount, cogs, 0, "مصروف وصل بعد بيع السيارة"));
                    break;
                case ChargeTreatment.Expense:
                    debits.Add(new(string.IsNullOrWhiteSpace(type?.AccountCode) ? s.VehicleExpenseAccount : type!.AccountCode!, line.CostAmount, 0));
                    break;
                default:
                    // على العميل: رسم يُمرَّر بتكلفته (أو مدفوع نيابة عنه) يُقفل عند فوترته، وإضافة لها سعر بيع تكلفتها تكلفة مبيعات إضافات
                    var passThrough = !line.SalePrice.HasValue || type?.IsDisbursement == true;
                    if (!passThrough) { debits.Add(new(s.AddOnCostAccount, line.CostAmount, 0)); break; }
                    // ما سيُفوتَر على العميل يُقيَّد على حسابه المؤقت ليقفله عند الفوترة تماماً، والفرق بين الرسم الفعلي والموعود على المعرض
                    var billed = line.SalePrice ?? line.CostAmount;
                    if (billed > 0) debits.Add(new(s.CustomerRechargeAccount, billed, 0));
                    if (line.CostAmount > billed) debits.Add(new(s.VehicleExpenseAccount, line.CostAmount - billed, 0, "فرق رسم محمَّل على العميل"));
                    else if (billed > line.CostAmount) debits.Add(new(s.VehicleExpenseAccount, 0, billed - line.CostAmount, "فرق رسم محمَّل على العميل"));
                    break;
            }
        if (inputVat > 0) debits.Add(new((await PostingAccounts.SettingsAsync(db, ct)).InputVat(), inputVat, 0));
        return debits;
    }

    private static async Task<string> CreditAccountAsync(ErpDbContext db, AdditionalChargeLine line, CarShowroomSettings s, CancellationToken ct)
    {
        if (line.Source == ChargeSource.Tamm) return s.TammWalletAccount;
        if (line.SupplierId.HasValue)
        {
            var account = await db.Set<Supplier>().AsNoTracking().Where(x => x.Id == line.SupplierId).Select(x => x.AccountCode).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(account)) return account;
        }
        return string.IsNullOrWhiteSpace(line.PaymentAccountCode) ? s.AccruedChargesAccount : line.PaymentAccountCode!;
    }

    private static bool PostsItself(AdditionalChargeLine line)
        => line.Source is not (ChargeSource.OpeningBalance or ChargeSource.OrderFields or ChargeSource.LandedCostVoucher);

    /// <summary>
    /// يرحّل قيد سطر فعلي لم يُرحَّل. الرصيد الافتتاحي لا قيد له، ورسوم حقول أمر الشراء يرحّلها الأمر عند الفوترة، وأسطر السند يرحّلها سندها.
    /// </summary>
    public static async Task PostAsync(ErpDbContext db, IAccountingPostingService posting, AdditionalChargeLine line, CancellationToken ct)
    {
        if (line.Status != ChargeStatus.Actual || line.JournalEntryId != null || !PostsItself(line)) return;
        var settings = await SalesCharges.SettingsAsync(db, ct);
        var type = await db.Set<ExpenseType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == line.ExpenseTypeId, ct);
        var lines = await DebitsAsync(db, line, type, settings, mark: true, ct);
        var total = lines.Sum(l => l.Debit - l.Credit);
        if (total <= 0) { await db.SaveChangesAsync(ct); return; }

        await EnsureAccountsAsync(db, settings, ct);
        lines.Add(new(await CreditAccountAsync(db, line, settings, ct), 0, total));
        var posted = await posting.PostAsync(new GenericPostingRequest
        {
            Date = line.Date, Description = $"{line.ExpenseTypeNameAr} - {line.DocumentNumber}".Trim(' ', '-'),
            SourceType = LineSource, SourceId = line.Id, SourceNumber = line.DocumentNumber, Lines = lines,
        }, ct);
        line.JournalEntryId = posted.JournalEntryId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>يعكس قيد السطر قبل تعديله أو إلغائه أو حذفه. حصة نُقلت إلى المخزون عند الاستلام يُعاد نقلها أولاً ليتطابق العكس.</summary>
    public static async Task UnpostAsync(ErpDbContext db, IAccountingPostingService posting, AdditionalChargeLine line, string reason, CancellationToken ct)
    {
        if (line.JournalEntryId == null || !PostsItself(line)) return;
        await posting.ReverseAsync(line.JournalEntryId.Value, reason, ct);
        await UndoReclassAsync(db, posting, new[] { line }, ct);
        line.JournalEntryId = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>قيد عكسه أعاد المبلغ إلى «في الطريق» بعد أن نُقل إلى المخزون: يُعاد النقل (مخزون ← في الطريق) فيتصفّر الحسابان.</summary>
    public static async Task UndoReclassAsync(ErpDbContext db, IAccountingPostingService posting, IEnumerable<AdditionalChargeLine> lines, CancellationToken ct)
    {
        var moved = lines.SelectMany(l => l.Allocations).Where(a => a.PostedInTransit && a.ReclassedToInventory).ToList();
        var amount = moved.Sum(a => a.Amount);
        foreach (var allocation in lines.SelectMany(l => l.Allocations)) { allocation.PostedInTransit = false; allocation.ReclassedToInventory = false; allocation.PostedToCogs = false; }
        if (amount <= 0) return;
        var s = await SalesCharges.SettingsAsync(db, ct);
        await posting.PostAsync(new GenericPostingRequest
        {
            Date = SaudiTime.Today, Description = "إعادة مصاريف سيارات مستلمة إلى حساب في الطريق لعكس قيدها", SourceType = ReceiptSource,
            Lines = new List<PostingLine> { new(s.VehiclesInTransitAccount, amount, 0), new(s.VehicleInventoryAccount, 0, amount) },
        }, ct);
    }

    /// <summary>استلام السيارات: ما قُيِّد عليها وهي في الطريق يُنقل من «سيارات في الطريق» إلى مخزون السيارات بالتكلفة.</summary>
    public static async Task ReclassOnReceiptAsync(ErpDbContext db, IAccountingPostingService posting, CarProcurementOrder order, IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        var pending = await db.Set<AdditionalChargeAllocation>()
            .Where(a => vehicleIds.Contains(a.VehicleId) && a.PostedInTransit && !a.ReclassedToInventory
                && a.Line.Status == ChargeStatus.Actual && a.Line.JournalEntryId != null).ToListAsync(ct);
        var amount = pending.Sum(a => a.Amount);
        if (amount <= 0) return;
        var s = await SalesCharges.SettingsAsync(db, ct);
        await EnsureAccountsAsync(db, s, ct);
        await posting.PostAsync(new GenericPostingRequest
        {
            Date = SaudiTime.Today, Description = $"استلام سيارات أمر {order.OrderNumber}: نقل مصاريفها من في الطريق إلى المخزون",
            SourceType = ReceiptSource, SourceId = order.Id, SourceNumber = order.OrderNumber,
            Lines = new List<PostingLine> { new(s.VehicleInventoryAccount, amount, 0), new(s.VehiclesInTransitAccount, 0, amount) },
        }, ct);
        foreach (var allocation in pending) allocation.ReclassedToInventory = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// قيد واحد لسند التكاليف الإضافية: مدين أسطره حسب معالجتها، ودائن المورد. سطر مستحق من حقول أمر الشراء رُحِّل سابقاً
    /// (مخزون مقابل مستحقات) وحلّ السند محله يُعكس أثره في القيد نفسه. يُرجع معرّف القيد أو null إن لم يكن ما يُرحَّل.
    /// </summary>
    /// <param name="fromAdvances">أسطر مسحوبة من تكاليف استيراد مقدمة: دائنها «تكاليف استيراد تحت التسوية» لا المورد (دُفعت سابقاً).</param>
    public static async Task<Guid?> PostVoucherAsync(ErpDbContext db, IAccountingPostingService posting, LandedCostVoucher voucher,
        IReadOnlyList<AdditionalChargeLine> charges, IEnumerable<AdditionalChargeLine> replacedPosted, CancellationToken ct,
        IReadOnlySet<AdditionalChargeLine>? fromAdvances = null)
    {
        var s = await SalesCharges.SettingsAsync(db, ct);
        var typeIds = charges.Select(c => c.ExpenseTypeId).Distinct().ToList();
        var types = await db.Set<ExpenseType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var lines = new List<PostingLine>();
        decimal settled = 0;
        foreach (var charge in charges)
        {
            var debits = await DebitsAsync(db, charge, types.GetValueOrDefault(charge.ExpenseTypeId), s, mark: true, ct);
            lines.AddRange(debits);
            if (fromAdvances?.Contains(charge) == true) settled += debits.Sum(l => l.Debit - l.Credit);
        }
        var total = lines.Sum(l => l.Debit - l.Credit);
        if (total <= 0) return null;

        if (settled > 0) lines.Add(new(s.ImportCostsClearingAccount, 0, settled, "تسوية تكاليف استيراد مدفوعة مقدماً"));
        var payable = total - settled;
        if (payable > 0)
        {
            var supplierAccount = await db.Set<Supplier>().AsNoTracking().Where(x => x.Id == voucher.SupplierId).Select(x => x.AccountCode).FirstOrDefaultAsync(ct);
            lines.Add(new(string.IsNullOrWhiteSpace(supplierAccount) ? s.AccruedChargesAccount : supplierAccount, 0, payable));
        }
        foreach (var replaced in replacedPosted.Where(l => l.JournalEntryId != null && l.CostAmount > 0))
        {
            lines.Add(new(s.AccruedChargesAccount, replaced.CostAmount, 0, $"إقفال مستحق {replaced.ExpenseTypeNameAr} حلّ السند محله"));
            // حصة سيارة بيعت خرجت من المخزون إلى تكلفة المبيعات: يُعكس المستحق من حيث صار
            var replacedVehicles = replaced.Allocations.Select(a => a.VehicleId).ToList();
            var soldIds = await db.Set<Vehicle>().Where(v => replacedVehicles.Contains(v.Id) && v.Status == VehicleStatus.Sold).Select(v => v.Id).ToListAsync(ct);
            var soldShare = replaced.Allocations.Where(a => soldIds.Contains(a.VehicleId)).Sum(a => a.Amount);
            if (replaced.CostAmount - soldShare > 0) lines.Add(new(s.VehicleInventoryAccount, 0, replaced.CostAmount - soldShare));
            if (soldShare > 0) lines.Add(new(s.CarCogsAccount, 0, soldShare));
        }
        await EnsureAccountsAsync(db, s, ct);
        var merged = lines.GroupBy(l => (l.AccountCode, IsDebit: l.Debit > 0, l.Notes))
            .Select(g => new PostingLine(g.Key.AccountCode, g.Sum(l => l.Debit), g.Sum(l => l.Credit), g.Key.Notes)).ToList();
        var posted = await posting.PostAsync(new GenericPostingRequest
        {
            Date = voucher.Date, Description = $"سند تكاليف إضافية {voucher.VoucherNumber} - {voucher.SupplierName}".TrimEnd(' ', '-'),
            SourceType = VoucherSource, SourceId = voucher.Id, SourceNumber = voucher.VoucherNumber, Lines = merged,
        }, ct);
        foreach (var charge in charges) charge.JournalEntryId = posted.JournalEntryId;
        await db.SaveChangesAsync(ct);
        return posted.JournalEntryId;
    }
}
