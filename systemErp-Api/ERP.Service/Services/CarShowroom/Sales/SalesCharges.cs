using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// مصاريف عقد البيع: الأسطر المحمّلة على العميل (رسوم وإضافات) تظهر في عرض السعر وتصير بنوداً في فاتورته،
/// وما على المعرض يبقى مصروفاً داخلياً. إجماليات العقد تُحدَّث مع كل تغيير في أسطره.
/// </summary>
internal static class SalesCharges
{
    public sealed record CustomerCharge(AdditionalChargeLine Line, decimal Price, decimal Vat);

    private static readonly (string Code, Func<CarSalesContract, decimal?> Amount)[] ContractFields =
    {
        ("PLATES", c => c.RegistrationFee),
        ("VEHICLE_INSURANCE", c => c.InsuranceCost),
        ("CUSTOMER_ADDONS", c => c.AccessoriesCost),
    };

    public static async Task<CarShowroomSettings> SettingsAsync(ErpDbContext db, CancellationToken ct)
        => await db.Set<CarShowroomSettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CarShowroomSettings();

    /// <summary>
    /// المعالجة الافتراضية للنوع: الرسم البيعي الذي أصله «على العميل» يتبع إعداد المعرض (على العميل أو على المعرض).
    /// </summary>
    public static ChargeTreatment DefaultTreatment(ExpenseType type, CarShowroomSettings settings)
        => type.Stage == ExpenseStage.Sale && type.DefaultTreatment == ChargeTreatment.RechargeToCustomer && settings.SaleFeesBorneBy == FeeBearer.Showroom
            ? ChargeTreatment.Expense : type.DefaultTreatment;

    /// <summary>لرسم بيعي يختار المستخدم «على العميل» أو «على المعرض» لكل سطر دون صلاحية خاصة.</summary>
    public static bool IsSaleFeeChoice(ExpenseType type, ChargeTreatment treatment)
        => type.Stage == ExpenseStage.Sale && treatment is ChargeTreatment.RechargeToCustomer or ChargeTreatment.Expense;

    public static IQueryable<AdditionalChargeLine> ContractLines(ErpDbContext db, Guid contractId)
        => db.Set<AdditionalChargeLine>().Where(l => l.DocumentType == ChargeDocumentTypes.SalesContract && l.DocumentId == contractId);

    /// <summary>الأسطر الظاهرة للعميل بسعرها وضريبتها: سعر البيع إن حُدِّد وإلا مبلغ السطر، والضريبة بنسبة الفئة «خاضع» من الإعدادات.</summary>
    public static async Task<List<CustomerCharge>> CustomerChargesAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        var lines = await ContractLines(db, contractId).AsNoTracking()
            .Where(l => l.Treatment == ChargeTreatment.RechargeToCustomer && (l.Status == ChargeStatus.Estimated || l.Status == ChargeStatus.Actual))
            .OrderBy(l => l.Date).ThenBy(l => l.CreatedAt).ToListAsync(ct);
        if (lines.Count == 0) return new();
        var rate = (await SettingsAsync(db, ct)).StandardVatRate;
        return lines.Select(l =>
        {
            var price = l.SalePrice ?? l.BaseAmount;
            return new CustomerCharge(l, price, l.VatCategory == VatCategory.Standard ? DocumentPricing.Round(price * rate / 100m) : 0);
        }).ToList();
    }

    /// <summary>يحدّث إجمالي ما على العميل في العقد. عقد مفوتَر أو ملغى لا يتغيّر.</summary>
    public static async Task RefreshContractAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        var contract = await db.Set<CarSalesContract>().FirstOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract == null || contract.Status is SalesContractStatus.Invoiced or SalesContractStatus.Cancelled or SalesContractStatus.Returned) return;
        var charges = await CustomerChargesAsync(db, contractId, ct);
        contract.CustomerChargesNet = charges.Sum(c => c.Price);
        contract.CustomerChargesVat = charges.Sum(c => c.Vat);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>بنود الفاتورة للرسوم والإضافات المحمّلة على العميل (سطر مستقل لكل منها).</summary>
    public static async Task<List<InvoiceItemDto>> InvoiceItemsAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        var charges = await CustomerChargesAsync(db, contractId, ct);
        if (charges.Count == 0) return new();
        var typeIds = charges.Select(c => c.Line.ExpenseTypeId).Distinct().ToList();
        var types = await db.Set<ExpenseType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var settings = await SettingsAsync(db, ct);
        var rate = settings.StandardVatRate;

        return charges.Select(c =>
        {
            var type = types[c.Line.ExpenseTypeId];
            // رسم يُمرَّر بتكلفته أو مدفوع نيابة عن العميل يُقفل حسابه ولا يدخل المبيعات؛ الإضافة بسعر بيع إيرادها إيراد إضافات
            var passThrough = !c.Line.SalePrice.HasValue || type.IsDisbursement;
            var standard = c.Line.VatCategory == VatCategory.Standard;
            // فئة غير خاضعة في فاتورة مبيعات يلزمها سبب برمز الهيئة: خارج النطاق له رمزه، وغيره من تعريف النوع
            var reason = standard ? null
                : !string.IsNullOrWhiteSpace(type.VatExemptionReasonCode) ? type.VatExemptionReasonCode
                : c.Line.VatCategory == VatCategory.OutOfScope ? "VATEX-SA-OOS"
                : throw new ValidationFailedException(string.Format(Messages.ChargeExemptionReasonRequired, type.NameAr));
            return new InvoiceItemDto
            {
                ItemId = Guid.Empty, ItemName = string.IsNullOrWhiteSpace(c.Line.Description) ? c.Line.ExpenseTypeNameAr : c.Line.Description!,
                Unit = "خدمة", Quantity = 1, UnitPrice = c.Price,
                VatRate = standard ? rate : 0, VatCategory = standard ? null : c.Line.VatCategory, VatExemptionReasonCode = reason,
                RevenueAccountCode = passThrough ? settings.CustomerRechargeAccount : settings.AddOnRevenueAccount,
            };
        }).ToList();
    }

    /// <summary>
    /// حقول العقد القديمة (رسوم التسجيل، التأمين، الملحقات) تُمثَّل بأسطر تقديرية يديرها العقد. كانت وصفية لا تُفوتَر،
    /// فتُسجَّل «على المعرض» حتى لا يتغيّر ما يُحمَّل على العميل؛ ما يُراد تحميله عليه يُضاف سطراً «على العميل».
    /// </summary>
    public static async Task SyncContractFieldsAsync(ErpDbContext db, CarSalesContract contract, CancellationToken ct)
    {
        if (contract.Status is SalesContractStatus.Invoiced or SalesContractStatus.Cancelled or SalesContractStatus.Returned) return;
        var existing = await ContractLines(db, contract.Id).Include(l => l.Allocations).Where(l => l.Source == ChargeSource.ContractFields).ToListAsync(ct);
        if (existing.Count == 0 && ContractFields.All(f => (f.Amount(contract) ?? 0) <= 0)) return;

        var codes = ContractFields.Select(f => f.Code).ToArray();
        var types = await db.Set<ExpenseType>().AsNoTracking().Where(t => codes.Contains(t.Code)).ToDictionaryAsync(t => t.Code, ct);
        if (types.Count != codes.Length)
        {
            await ExpenseTypeSeed.EnsureAsync(db, ct);
            types = await db.Set<ExpenseType>().AsNoTracking().Where(t => codes.Contains(t.Code)).ToDictionaryAsync(t => t.Code, ct);
        }
        var currency = await db.Set<Currency>().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR";

        foreach (var (code, field) in ContractFields)
        {
            var amount = field(contract) ?? 0;
            var line = existing.FirstOrDefault(l => l.ExpenseTypeCode == code);
            if (line is { Status: ChargeStatus.Replaced or ChargeStatus.Cancelled or ChargeStatus.Actual }) continue;
            if (amount <= 0)
            {
                if (line == null) continue;
                db.RemoveRange(line.Allocations);
                db.Remove(line);
                continue;
            }
            if (line == null)
            {
                var type = types[code];
                line = new AdditionalChargeLine
                {
                    DocumentType = ChargeDocumentTypes.SalesContract, DocumentId = contract.Id,
                    ExpenseTypeId = type.Id, ExpenseTypeCode = type.Code, ExpenseTypeNameAr = type.NameAr, Stage = type.Stage,
                    VatCategory = type.VatCategory, Treatment = ChargeTreatment.Expense, Status = ChargeStatus.Estimated,
                    Source = ChargeSource.ContractFields, AllocationMethod = ChargeAllocationMethod.Equal, CurrencyCode = currency, ExchangeRate = 1,
                };
                db.Add(line);
            }
            line.DocumentNumber = contract.ContractNumber; line.Date = contract.Date;
            line.Amount = amount; line.BaseAmount = amount; line.CostAmount = amount;
            var allocation = line.Allocations.FirstOrDefault();
            if (allocation == null)
            {
                allocation = new AdditionalChargeAllocation { VehicleId = contract.VehicleId };
                line.Allocations.Add(allocation);
                if (db.Entry(line).State != EntityState.Added) db.Add(allocation);
            }
            allocation.VehicleId = contract.VehicleId; allocation.Amount = amount;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>تبديل سيارة العقد عند التخصيص: تقديراته تنتقل للسيارة الجديدة، والفعلي يبقى على السيارة التي صُرف عليها.</summary>
    public static async Task MoveEstimatesAsync(ErpDbContext db, Guid contractId, Guid vehicleId, CancellationToken ct)
    {
        var lines = await ContractLines(db, contractId).Include(l => l.Allocations).Where(l => l.Status == ChargeStatus.Estimated).ToListAsync(ct);
        foreach (var allocation in lines.SelectMany(l => l.Allocations)) allocation.VehicleId = vehicleId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>إلغاء العقد يلغي تقديراته؛ ما صُرف فعلاً يبقى مسجَّلاً.</summary>
    public static async Task CancelEstimatesAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        foreach (var line in await ContractLines(db, contractId).Where(l => l.Status == ChargeStatus.Estimated).ToListAsync(ct))
            line.Status = ChargeStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }
}
