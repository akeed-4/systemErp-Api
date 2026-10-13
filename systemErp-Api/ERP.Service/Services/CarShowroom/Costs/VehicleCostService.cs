using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class VehicleCostService : IVehicleCostService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public VehicleCostService(ErpDbContext db, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _tx = tx; _audit = audit;
    }

    public async Task<VehicleCostCardDto> GetCostCardAsync(Guid vehicleId, CancellationToken ct = default)
    {
        if (!await _db.Set<Vehicle>().AnyAsync(v => v.Id == vehicleId, ct)) throw new NotFoundException(Messages.VehicleNotFound);
        // مركبة سابقة لسجل المصاريف: يُكمَّل رصيدها الافتتاحي أولاً ليطابق الكرتُ تكلفتَها
        await VehicleCostLedger.EnsureOpeningAsync(_db, new[] { vehicleId }, ct);

        var v = await _db.Set<Vehicle>().AsNoTracking().FirstAsync(x => x.Id == vehicleId, ct);
        var allocations = await _db.Set<AdditionalChargeAllocation>().AsNoTracking().Include(a => a.Line)
            .Where(a => a.VehicleId == vehicleId).OrderBy(a => a.Line.Date).ThenBy(a => a.Line.CreatedAt).ToListAsync(ct);

        // حصة السيارة من السطر التقديري الذي حلّ الفعلي محلّه
        var replacedIds = allocations.Where(a => a.Line.ReplacesLineId.HasValue).Select(a => a.Line.ReplacesLineId!.Value).ToList();
        var replaced = allocations.Where(a => replacedIds.Contains(a.LineId)).ToDictionary(a => a.LineId, a => a.Amount);

        decimal Sum(ChargeStatus status, ChargeTreatment treatment)
            => allocations.Where(a => a.Line.Status == status && a.Line.Treatment == treatment && !replacedIds.Contains(a.LineId)).Sum(a => a.Amount);

        var card = new VehicleCostCardDto
        {
            VehicleId = v.Id, ChassisNumber = v.ChassisNumber, Status = v.Status,
            Description = $"{v.BrandNameAr} {v.ModelNameAr} {v.TrimNameAr} {v.Year}".Replace("  ", " ").Trim(),
            PurchasePrice = v.PurchasePrice ?? 0, AdditionalCosts = v.AdditionalCosts ?? 0, PreparationCost = v.PreparationCost ?? 0,
            TotalCost = v.TotalCost, SellingPrice = v.SellingPrice,
            EstimatedCapitalized = Sum(ChargeStatus.Estimated, ChargeTreatment.Capitalize),
            EstimatedExpensed = Sum(ChargeStatus.Estimated, ChargeTreatment.Expense), ActualExpensed = Sum(ChargeStatus.Actual, ChargeTreatment.Expense),
            EstimatedRecharged = Sum(ChargeStatus.Estimated, ChargeTreatment.RechargeToCustomer), ActualRecharged = Sum(ChargeStatus.Actual, ChargeTreatment.RechargeToCustomer),
            Lines = allocations.Where(a => !replacedIds.Contains(a.LineId)).Select(a => new VehicleCostCardLineDto
            {
                LineId = a.LineId, Date = a.Line.Date, ExpenseTypeCode = a.Line.ExpenseTypeCode, ExpenseTypeNameAr = a.Line.ExpenseTypeNameAr,
                Stage = a.Line.Stage, Treatment = a.Line.Treatment, Status = a.Line.Status, Source = a.Line.Source,
                DocumentType = a.Line.DocumentType, DocumentId = a.Line.DocumentId, DocumentNumber = a.Line.DocumentNumber,
                SupplierName = a.Line.SupplierName, Description = a.Line.Description,
                EstimatedAmount = a.Line.Status == ChargeStatus.Estimated ? a.Amount
                    : a.Line.ReplacesLineId.HasValue && replaced.TryGetValue(a.Line.ReplacesLineId.Value, out var estimated) ? estimated
                    // تقديري تحقق بمبلغ مختلف: حصة السيارة من قيمته التقديرية بنسبة حصتها من الفعلي
                    : a.Line.EstimatedCostAmount.HasValue
                        ? (a.Line.CostAmount == 0 ? a.Line.EstimatedCostAmount : DocumentPricing.Round(a.Line.EstimatedCostAmount.Value * a.Amount / a.Line.CostAmount))
                    : null,
                ActualAmount = a.Line.Status == ChargeStatus.Actual ? a.Amount : null,
            }).ToList(),
        };
        card.ExpectedTotalCost = card.TotalCost + card.EstimatedCapitalized;
        return card;
    }

    public async Task<List<VehicleCostAdjustmentDto>> GetAdjustmentsAsync(Guid vehicleId, CancellationToken ct = default)
        => (await _db.Set<VehicleCostAdjustment>().AsNoTracking().Where(a => a.VehicleId == vehicleId).OrderBy(a => a.Date).ThenBy(a => a.AdjustmentNumber).ToListAsync(ct))
            .Select(Mapper.Map<VehicleCostAdjustmentDto>).ToList();

    public async Task<ProcurementExpectedCostDto> GetProcurementExpectedCostAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _db.Set<CarProcurementOrder>().AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException(Messages.ProcurementOrderNotFound);
        var fx = order.ExchangeRate <= 0 ? 1 : order.ExchangeRate;
        var vehicleIds = await ProcurementCharges.VehicleIdsAsync(_db, orderId, ct);

        // أسطر الأمر نفسه، وكل سطر آخر (على السيارة أو من سند تكاليف) له حصة على إحدى سياراته
        var lines = await _db.Set<AdditionalChargeLine>().AsNoTracking().Include(l => l.Allocations)
            .Where(l => (l.Status == ChargeStatus.Estimated || l.Status == ChargeStatus.Actual)
                && ((l.DocumentType == ChargeDocumentTypes.ProcurementOrder && l.DocumentId == orderId) || l.Allocations.Any(a => vehicleIds.Contains(a.VehicleId))))
            .ToListAsync(ct);
        decimal Share(AdditionalChargeLine l) => l.Allocations.Count == 0 ? l.CostAmount : l.Allocations.Where(a => vehicleIds.Contains(a.VehicleId)).Sum(a => a.Amount);
        decimal Sum(ChargeStatus status) => lines.Where(l => l.Status == status && l.Treatment == ChargeTreatment.Capitalize).Sum(Share);

        var dto = new ProcurementExpectedCostDto
        {
            OrderId = order.Id, OrderNumber = order.OrderNumber, VehiclesCount = order.Items.Sum(i => i.Quantity),
            PurchaseTotal = order.Items.Sum(i => DocumentPricing.Round(i.TotalBeforeVat * fx)),
            EstimatedCharges = Sum(ChargeStatus.Estimated), ActualCharges = Sum(ChargeStatus.Actual),
            NonCapitalizedCharges = lines.Where(l => l.Treatment != ChargeTreatment.Capitalize).Sum(Share),
        };
        dto.ExpectedTotalCost = dto.PurchaseTotal + dto.EstimatedCharges + dto.ActualCharges;
        return dto;
    }

    public Task<int> MigrateLegacyCostsAsync(CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var created = await VehicleCostLedger.EnsureOpeningAsync(_db, null, token);
            if (created > 0)
                await _audit.LogAsync("LEGACY_COSTS_MIGRATED", nameof(Vehicle), string.Empty, $"تحويل أرصدة تكلفة المركبات إلى {created} سطر رصيد افتتاحي", token);
            return created;
        }, ct);
}
