using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>حساب ربح السيارة. دالة صِرفة: كل المبالغ قبل الضريبة وبالعملة الأساسية.</summary>
public static class VehicleProfit
{
    /// <summary>
    /// الربح = (صافي سعر السيارة + ما على العميل من رسوم وإضافات) − (تكلفة السيارة + ما سيُضاف لتكلفتها + مصاريف المعرض عليها + تكلفة ما حُمّل على العميل).
    /// النسبة من إجمالي الإيراد؛ صفر حين لا إيراد.
    /// </summary>
    public static (decimal Profit, decimal Percent) Compute(decimal vehicleNetPrice, decimal customerChargesNet, decimal vehicleCost,
        decimal pendingCapitalized, decimal showroomExpenses, decimal customerChargesCost)
    {
        var revenue = vehicleNetPrice + customerChargesNet;
        var profit = revenue - (vehicleCost + pendingCapitalized + showroomExpenses + customerChargesCost);
        return (profit, revenue == 0 ? 0 : Math.Round(profit / revenue * 100, 2, MidpointRounding.AwayFromZero));
    }
}

public class SaleProfitService : ISaleProfitService
{
    private readonly ErpDbContext _db;
    public SaleProfitService(ErpDbContext db) => _db = db;

    public async Task<List<SaleCustomerChargeDto>> GetCustomerChargesAsync(Guid contractId, CancellationToken ct = default)
    {
        if (!await _db.Set<CarSalesContract>().AnyAsync(c => c.Id == contractId, ct)) throw new NotFoundException(Messages.SalesContractNotFound);
        return (await SalesCharges.CustomerChargesAsync(_db, contractId, ct)).Select(ToDto).ToList();
    }

    private static SaleCustomerChargeDto ToDto(SalesCharges.CustomerCharge c) => new()
    {
        LineId = c.Line.Id, Name = string.IsNullOrWhiteSpace(c.Line.Description) ? c.Line.ExpenseTypeNameAr : c.Line.Description!,
        Status = c.Line.Status, Price = c.Price, VatAmount = c.Vat, Total = c.Price + c.Vat, VatCategory = c.Line.VatCategory,
    };

    public async Task<SaleProfitPanelDto> GetPanelAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await _db.Set<CarSalesContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.SalesContractNotFound);
        var vehicle = await _db.Set<Vehicle>().AsNoTracking().FirstAsync(v => v.Id == contract.VehicleId, ct);
        var charges = await SalesCharges.CustomerChargesAsync(_db, contractId, ct);
        var allocations = await _db.Set<AdditionalChargeAllocation>().AsNoTracking().Include(a => a.Line)
            .Where(a => a.VehicleId == vehicle.Id && (a.Line.Status == ChargeStatus.Estimated || a.Line.Status == ChargeStatus.Actual)).ToListAsync(ct);

        // بعد الفوترة التكلفة هي ما أُثبت في تكلفة المبيعات؛ قبلها تكلفة السيارة الحالية
        var invoiced = contract.Status == SalesContractStatus.Invoiced;
        var panel = new SaleProfitPanelDto
        {
            ContractId = contract.Id, VehicleId = vehicle.Id,
            VehicleNetPrice = contract.NetPriceBeforeVat ?? contract.SellingPrice - (contract.DiscountAmount ?? 0), VehicleVat = contract.VatAmount,
            CustomerChargesNet = charges.Sum(c => c.Price), CustomerChargesVat = charges.Sum(c => c.Vat),
            CustomerCharges = charges.Select(ToDto).ToList(),
            CurrentCost = invoiced ? contract.CostPrice : vehicle.TotalCost,
            PendingCapitalized = invoiced ? 0 : allocations.Where(a => a.Line.Status == ChargeStatus.Estimated && a.Line.Treatment == ChargeTreatment.Capitalize).Sum(a => a.Amount),
            ShowroomExpenses = allocations.Where(a => a.Line.Treatment == ChargeTreatment.Expense).Sum(a => a.Amount),
            CustomerChargesCost = charges.Sum(c => c.Line.CostAmount),
        };
        panel.CustomerTotal = contract.TotalWithVat + panel.CustomerChargesNet + panel.CustomerChargesVat;
        (panel.ExpectedProfit, panel.ExpectedProfitPercent) = VehicleProfit.Compute(panel.VehicleNetPrice, panel.CustomerChargesNet, panel.CurrentCost,
            panel.PendingCapitalized, panel.ShowroomExpenses, panel.CustomerChargesCost);
        return panel;
    }
}

public class CarShowroomSettingsService : ICarShowroomSettingsService
{
    private readonly ErpDbContext _db;
    public CarShowroomSettingsService(ErpDbContext db) => _db = db;

    public async Task<CarShowroomSettingsDto> GetAsync(CancellationToken ct = default)
        => Mapper.Map<CarShowroomSettingsDto>(await SalesCharges.SettingsAsync(_db, ct));

    public async Task<CarShowroomSettingsDto> UpdateAsync(CarShowroomSettingsDto dto, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(dto.SaleFeesBorneBy) || !Enum.IsDefined(dto.MarginBasis) || !Enum.IsDefined(dto.DepositCancellationPolicy) || dto.ReservationDays < 1 || dto.StandardVatRate is < 0 or > 100)
            throw new ValidationFailedException(Messages.SettingsValuesInvalid);
        var settings = await _db.Set<CarShowroomSettings>().FirstOrDefaultAsync(ct);
        if (settings == null)
        {
            settings = new CarShowroomSettings();
            _db.Add(settings);
        }
        settings.SaleFeesBorneBy = dto.SaleFeesBorneBy; settings.ReservationDays = dto.ReservationDays;
        settings.StandardVatRate = dto.StandardVatRate; settings.MarginBasis = dto.MarginBasis;
        settings.DepositCancellationPolicy = dto.DepositCancellationPolicy; settings.RefundCustomerChargesOnReturn = dto.RefundCustomerChargesOnReturn;

        // الحسابات: الفارغ يبقى كما هو، والمُرسل يجب أن يكون حساباً نهائياً موجوداً في شجرة الحسابات
        await ChargePosting.EnsureAccountsAsync(_db, settings, ct);
        foreach (var property in typeof(CarShowroomSettingsDto).GetProperties().Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Account")))
        {
            var code = (property.GetValue(dto) as string)?.Trim();
            if (string.IsNullOrEmpty(code)) continue;
            if (!await _db.Set<Account>().AnyAsync(a => a.Code == code, ct) || await _db.Set<Account>().AnyAsync(a => a.ParentCode == code, ct))
                throw new ValidationFailedException(string.Format(Messages.SettingsAccountInvalid, code));
            typeof(CarShowroomSettings).GetProperty(property.Name)!.SetValue(settings, code);
        }
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<CarShowroomSettingsDto>(settings);
    }
}
