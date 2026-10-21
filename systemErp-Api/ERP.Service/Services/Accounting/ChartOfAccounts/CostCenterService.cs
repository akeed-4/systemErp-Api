using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class CostCenterService : CrudService<CostCenter, CostCenterDto, CreateCostCenterDto, UpdateCostCenterDto>, ICostCenterService
{
    public CostCenterService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelCostCenter;

    protected override IQueryable<CostCenter> ApplySearch(IQueryable<CostCenter> q, string t)
        => q.Where(c => c.Code.Contains(t) || c.NameAr.Contains(t) || c.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateCostCenterDto dto, CostCenter? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.NameAr))
            throw new ValidationFailedException(Messages.CodeAndArabicNameRequired);
        if (await Db.Set<CostCenter>().AnyAsync(c => c.Code == dto.Code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException(Messages.CostCenterCodeInUse);
    }

    /// <summary>
    /// المركز المستخدم لا يُحذف: القيود أولاً، ثم كل مستند يشير إليه (بعضها بلا قيد قاعدة بيانات كبنود الفواتير والعقود والمسيّرات)
    /// برسالة تسمّي موضع الاستخدام، ويُقترح إيقافه بدلاً من الحذف.
    /// </summary>
    protected override async Task OnDeletingAsync(CostCenter entity, CancellationToken ct)
    {
        var id = entity.Id;
        if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.CostCenterId == id, ct))
            throw new ConflictException(Messages.CannotDeleteCostCenterWithEntries);

        string? usage =
            await Db.Set<FixedAsset>().AnyAsync(a => a.CostCenterId == id, ct)
                || await Db.Set<FixedAssetDepreciation>().AnyAsync(d => d.CostCenterId == id, ct) ? Messages.CostCenterUsageFixedAssets
            : await Db.Set<Employee>().AnyAsync(e => e.CostCenterId == id, ct) ? Messages.CostCenterUsageEmployees
            : await Db.Set<PayrollLine>().AnyAsync(p => p.CostCenterId == id, ct) ? Messages.CostCenterUsagePayroll
            : await Db.Set<CommercialContract>().AnyAsync(c => c.CostCenterId == id, ct) ? Messages.CostCenterUsageContracts
            : await Db.Set<InvoiceItemBase>().AnyAsync(i => i.CostCenterId == id, ct) ? Messages.CostCenterUsageInvoices
            : null;
        if (usage != null) throw new ConflictException(string.Format(Messages.CannotDeleteCostCenterInUse, usage));
    }
}
