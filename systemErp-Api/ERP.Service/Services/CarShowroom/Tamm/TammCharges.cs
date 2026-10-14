using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.CarShowroom;

/// <summary>مصاريف عمليات تم في سجل المصاريف: إنشاؤها عند نجاح العملية، ونقل مصروف الاستعلام إلى تكلفة السيارة حين تُشترى.</summary>
internal static class TammCharges
{
    /// <summary>نوع المصروف المقابل لكل عملية (أكواد الأنواع المزروعة ثابتة).</summary>
    private static readonly Dictionary<TammOperationType, string> TypeCodes = new()
    {
        [TammOperationType.TransferToShowroom] = "TAMM_TRANSFER_IN",
        [TammOperationType.Hold] = "TAMM_HOLD",
        [TammOperationType.AutoHold] = "TAMM_AUTO_HOLD",
        [TammOperationType.ViolationsInquiry] = "TAMM_VIOLATIONS",
        [TammOperationType.RegistrationIssue] = "TAMM_REG_ISSUE",
        [TammOperationType.RegistrationRenewal] = "TAMM_REG_RENEW",
        [TammOperationType.InternalAuthorization] = "TAMM_AUTH_INTERNAL",
        [TammOperationType.ExternalAuthorization] = "TAMM_AUTH_EXTERNAL",
        [TammOperationType.ActualDriver] = "TAMM_ACTUAL_DRIVER",
        [TammOperationType.TransferToBuyer] = "TAMM_TRANSFER_OUT",
    };

    private static async Task<ExpenseType> TypeAsync(ErpDbContext db, TammOperationType operation, CancellationToken ct)
    {
        var code = TypeCodes[operation];
        var type = await db.Set<ExpenseType>().AsNoTracking().FirstOrDefaultAsync(t => t.Code == code, ct);
        if (type != null) return type;
        await ExpenseTypeSeed.EnsureAsync(db, ct);
        return await db.Set<ExpenseType>().AsNoTracking().FirstAsync(t => t.Code == code, ct);
    }

    /// <summary>
    /// مصروف العملية الناجحة. سيارة في النظام: سطر عليها بمعالجة نوعه، ولنقل الملكية للمشتري بالمعالجة المختارة في العقد
    /// (يحل محل تقديره). سيارة ليست في النظام (استعلام قبل الشراء): مصروف عام. لا يُنشأ مرتين للعملية نفسها.
    /// </summary>
    public static async Task CreateAsync(ErpDbContext db, IAccountingPostingService posting, ERP.Core.Contracts.Shared.INumberSequenceService numbers, TammOperation op, CancellationToken ct)
    {
        if (op.Fee <= 0) return;
        if (await db.Set<AdditionalChargeLine>().AnyAsync(l => l.TammOperationId == op.Id, ct)) return;

        var type = await TypeAsync(db, op.Type, ct);
        var settings = await SalesCharges.SettingsAsync(db, ct);
        if (!op.VehicleId.HasValue && op.Vin != null)
            op.VehicleId = await db.Set<Vehicle>().Where(v => v.ChassisNumber == op.Vin).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);
        var vehicle = op.VehicleId.HasValue ? await db.Set<Vehicle>().AsNoTracking().FirstOrDefaultAsync(v => v.Id == op.VehicleId, ct) : null;

        var line = new AdditionalChargeLine
        {
            ExpenseTypeId = type.Id, ExpenseTypeCode = type.Code, ExpenseTypeNameAr = type.NameAr, Stage = type.Stage,
            Date = op.CompletedAt ?? SaudiTime.Now, Description = $"عملية تم {op.OperationNumber}",
            Amount = op.Fee, BaseAmount = op.Fee, CostAmount = op.Fee, ExchangeRate = 1,
            CurrencyCode = await db.Set<Currency>().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR",
            VatCategory = type.VatCategory, IsVatRecoverable = type.IsVatRecoverable, Status = ChargeStatus.Actual, Source = ChargeSource.Tamm,
            AllocationMethod = ChargeAllocationMethod.Equal, TammOperationId = op.Id, Notes = op.Reference,
        };

        Guid? contractId = null;
        if (vehicle == null)
        {
            // سيارة لم تُشترَ: مصروف عام على المعرض؛ يُنقل لتكلفتها إن اشتُريت لاحقاً
            line.DocumentType = ChargeDocumentTypes.TammOperation; line.DocumentId = op.Id; line.DocumentNumber = op.OperationNumber;
            line.Treatment = ChargeTreatment.Expense;
        }
        else
        {
            var treatment = SalesCharges.DefaultTreatment(type, settings);
            var contract = op.ContractId.HasValue ? await db.Set<CarSalesContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == op.ContractId, ct) : null;
            if (contract != null)
            {
                contractId = contract.Id;
                line.DocumentType = ChargeDocumentTypes.SalesContract; line.DocumentId = contract.Id; line.DocumentNumber = contract.ContractNumber;
                var estimated = await SalesCharges.ContractLines(db, contract.Id)
                    .Where(l => l.ExpenseTypeId == type.Id && l.Status == ChargeStatus.Estimated).OrderBy(l => l.CreatedAt).FirstOrDefaultAsync(ct);
                if (estimated != null)
                {
                    // المعالجة التي اختارها المستخدم في العقد؛ وما وُعد به العميل سعراً يبقى سعره ولو اختلف الرسم الفعلي
                    treatment = estimated.Treatment;
                    if (treatment == ChargeTreatment.RechargeToCustomer) line.SalePrice = estimated.SalePrice ?? estimated.BaseAmount;
                    line.ReplacesLineId = estimated.Id;
                    estimated.Status = ChargeStatus.Replaced;
                }
                else if (treatment == ChargeTreatment.RechargeToCustomer && contract.Status is SalesContractStatus.Invoiced or SalesContractStatus.Returned)
                    treatment = ChargeTreatment.Expense; // صدرت الفاتورة بلا هذا الرسم: يتحمله المعرض
            }
            else
            {
                line.DocumentType = ChargeDocumentTypes.Vehicle; line.DocumentId = vehicle.Id; line.DocumentNumber = vehicle.ChassisNumber;
                if (treatment == ChargeTreatment.RechargeToCustomer) treatment = ChargeTreatment.Expense; // لا عقد يُحمَّل عليه
            }
            // سيارة مشطوبة أو أُلغي شراؤها لا تُمسّ تكلفتها: الرسم مصروف على المعرض. المباعة يُحمَّل رسمها على تكلفة المبيعات بقيد تعديل موثق
            if (treatment == ChargeTreatment.Capitalize && vehicle.Status is VehicleStatus.WrittenOff or VehicleStatus.Cancelled)
                treatment = ChargeTreatment.Expense;
            line.Treatment = treatment;
            if (treatment == ChargeTreatment.Capitalize) await VehicleCostLedger.EnsureOpeningAsync(db, new[] { vehicle.Id }, ct);
            line.Allocations.Add(new AdditionalChargeAllocation { VehicleId = vehicle.Id, Amount = line.CostAmount });
        }

        db.Add(line);
        await db.SaveChangesAsync(ct);
        if (vehicle != null && line.Treatment == ChargeTreatment.Capitalize)
        {
            await VehicleCostLedger.RecomputeAsync(db, new[] { vehicle.Id }, ct);
            await db.SaveChangesAsync(ct);
        }
        // القيد: المدين حسب المعالجة، والدائن رصيد محفظة تم؛ وتُسجَّل الحركة في كشف المحفظة
        await ChargePosting.PostAsync(db, posting, line, ct);
        if (vehicle is { Status: VehicleStatus.Sold } && line.Treatment == ChargeTreatment.Capitalize)
            await SoldVehicleAdjustments.RecordAsync(db, numbers, null, new[] { new KeyValuePair<Guid, decimal>(vehicle.Id, line.CostAmount) },
                $"عملية تم {op.OperationNumber} بعد بيع السيارة", "tamm", line.Id, line.JournalEntryId, ct);
        if (line.JournalEntryId != null)
        {
            db.Add(new TammWalletTransaction
            {
                Date = line.Date, Type = TammWalletTransactionType.Charge, Amount = -op.Fee, TammOperationId = op.Id,
                Reference = op.Reference ?? op.OperationNumber, Notes = $"{type.NameAr} - {op.Vin}", JournalEntryId = line.JournalEntryId,
            });
            await db.SaveChangesAsync(ct);
        }
        if (contractId.HasValue) await SalesCharges.RefreshContractAsync(db, contractId.Value, ct);
    }

    /// <summary>
    /// سيارات دخلت النظام للتو (شراء أو استلام): عمليات تم السابقة على أرقام شواسيها تُربط بها،
    /// ومصاريفها المسجَّلة «مصروفاً عاماً» تُنقل إلى تكلفتها.
    /// </summary>
    public static async Task AttachPurchasedAsync(ErpDbContext db, IAccountingPostingService posting, IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        var vehicles = await db.Set<Vehicle>().AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).Select(v => new { v.Id, v.ChassisNumber }).ToListAsync(ct);
        var vins = vehicles.Select(v => v.ChassisNumber).ToList();
        var operations = await db.Set<TammOperation>().Where(o => o.VehicleId == null && o.Vin != null && vins.Contains(o.Vin)).ToListAsync(ct);
        if (operations.Count == 0) return;

        var byVin = vehicles.ToDictionary(v => v.ChassisNumber, v => v.Id);
        foreach (var op in operations) op.VehicleId = byVin[op.Vin!];
        var opIds = operations.Select(o => (Guid?)o.Id).ToList();
        var lines = await db.Set<AdditionalChargeLine>().Include(l => l.Allocations)
            .Where(l => opIds.Contains(l.TammOperationId) && l.DocumentType == ChargeDocumentTypes.TammOperation && l.Status == ChargeStatus.Actual).ToListAsync(ct);
        var touched = new List<Guid>();
        foreach (var line in lines)
        {
            var op = operations.First(o => o.Id == line.TammOperationId);
            var vehicleId = op.VehicleId!.Value;
            // القيد: يُعكس المصروف العام ثم يُرحَّل على تكلفة السيارة (رصيد المحفظة لا يتغيّر)
            await ChargePosting.UnpostAsync(db, posting, line, "نقل استعلام تم إلى تكلفة السيارة بعد شرائها", ct);
            line.DocumentType = ChargeDocumentTypes.Vehicle; line.DocumentId = vehicleId; line.DocumentNumber = op.Vin;
            line.Treatment = ChargeTreatment.Capitalize;
            line.Notes = string.IsNullOrWhiteSpace(line.Notes) ? "نُقل من مصروف عام إلى تكلفة السيارة بعد شرائها" : $"{line.Notes} - نُقل من مصروف عام إلى تكلفة السيارة بعد شرائها";
            var allocation = new AdditionalChargeAllocation { VehicleId = vehicleId, Amount = line.CostAmount };
            line.Allocations.Add(allocation);
            db.Add(allocation);
            touched.Add(vehicleId);
        }
        await db.SaveChangesAsync(ct);
        if (touched.Count == 0) return;
        await VehicleCostLedger.RecomputeAsync(db, touched.Distinct().ToList(), ct);
        await db.SaveChangesAsync(ct);
        foreach (var line in lines) await ChargePosting.PostAsync(db, posting, line, ct);
    }
}
