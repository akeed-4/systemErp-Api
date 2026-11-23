using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class CostCenterService : CrudService<CostCenter, CostCenterDto, CreateCostCenterDto, UpdateCostCenterDto>, ICostCenterService
{
    /// <summary>عرض تسلسل المركز الرئيسي (01..99) وتسلسل الفرع المباشر للمركز الرئيسي (11 ← 1101).</summary>
    internal const int MainSegmentWidth = 2;
    /// <summary>عرض تسلسل ما تحت المستوى الثاني فأعمق (1101 ← 11010001).</summary>
    internal const int SubSegmentWidth = 4;

    private readonly INumberSequenceService _numbers;

    public CostCenterService(ErpDbContext db, INumberSequenceService numbers) : base(db) { _numbers = numbers; }
    protected override string Label => Messages.LabelCostCenter;
    /// <summary>حجز الرقم وحفظ المركز معاً: لا يُستهلك رقم لمركز لم يُحفظ.</summary>
    protected override bool Transactional => true;

    protected override IQueryable<CostCenter> ApplySearch(IQueryable<CostCenter> q, string t)
        => q.Where(c => c.Code.Contains(t) || c.NameAr.Contains(t) || c.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateCostCenterDto dto, CostCenter? existing, CancellationToken ct)
    {
        // الرمز اختياري عند الإنشاء (يولّده الخادم من الأب)، وتركه فارغاً عند التعديل يبقي الرمز الحالي.
        dto.Code = dto.Code?.Trim() ?? string.Empty;
        if (existing != null && dto.Code.Length == 0) dto.Code = existing.Code;
        if (string.IsNullOrWhiteSpace(dto.NameAr)) throw new ValidationFailedException(Messages.ArabicNameRequired);
        if (dto.Code.Length > 0 && await Db.Set<CostCenter>().AnyAsync(c => c.Code == dto.Code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException(Messages.CostCenterCodeInUse);
        if (dto.ParentId is { } parentId)
        {
            // الأب من المنشأة نفسها (مرشّح المنشأة)، ولا يكون المركز نفسه ولا أحد فروعه فيدور الهرم
            if (!await Db.Set<CostCenter>().AnyAsync(c => c.Id == parentId, ct))
                throw new ValidationFailedException(Messages.CostCenterParentNotFound);
            if (existing != null && (await CostCenterRules.ScopeAsync(Db, existing.Id, ct))!.Contains(parentId))
                throw new ValidationFailedException(Messages.CostCenterParentInvalid);
        }
    }

    protected override async Task OnCreatingAsync(CostCenter entity, CreateCostCenterDto dto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(entity.Code)) entity.Code = await NextCodeAsync(entity.ParentId, ct);
    }

    /// <summary>
    /// عرض تسلسل الابن: المركز الرئيسي (بلا أب) وابن المركز الرئيسي خانتان، وما تحت ذلك أربع خانات.
    /// </summary>
    internal static int SegmentWidth(bool hasParent, bool parentIsRoot) => !hasParent || parentIsRoot ? MainSegmentWidth : SubSegmentWidth;

    /// <summary>
    /// الرمز التالي تحت الأب المختار: رمز الأب + تسلسل (01 ← 0101 ← 01010001). المركز القديم برمز حر (CC-001) يبقى كما هو،
    /// وأبناؤه الجدد يأخذون رمزه بادئة. العدّاد لكل أب فلا يُعاد رقم مركز محذوف.
    /// </summary>
    private async Task<string> NextCodeAsync(Guid? parentId, CancellationToken ct)
    {
        var prefix = string.Empty;
        var parentIsRoot = false;
        if (parentId is { } id)
        {
            var parent = await Db.Set<CostCenter>().AsNoTracking().Where(c => c.Id == id)
                .Select(c => new { c.Code, c.ParentId }).FirstOrDefaultAsync(ct)
                ?? throw new ValidationFailedException(Messages.CostCenterParentNotFound);
            prefix = parent.Code;
            parentIsRoot = parent.ParentId == null;
        }
        var width = SegmentWidth(parentId.HasValue, parentIsRoot);
        var key = $"cost_center_code:{(parentId?.ToString("N") ?? "root")}";
        return await HierarchicalCodes.NextAsync(Db, _numbers, key, prefix, width, Db.Set<CostCenter>().Select(c => c.Code),
            string.Format(Messages.CostCenterCodesExhausted, prefix.Length > 0 ? prefix : "—"), ct);
    }

    /// <summary>
    /// المركز المستخدم لا يُحذف: القيود أولاً، ثم كل مستند يشير إليه (بعضها بلا قيد قاعدة بيانات كبنود الفواتير والعقود والمسيّرات)
    /// برسالة تسمّي موضع الاستخدام، ويُقترح إيقافه بدلاً من الحذف.
    /// </summary>
    protected override async Task OnDeletingAsync(CostCenter entity, CancellationToken ct)
    {
        var id = entity.Id;
        if (await Db.Set<CostCenter>().AnyAsync(c => c.ParentId == id, ct))
            throw new ConflictException(Messages.CannotDeleteCostCenterWithChildren);
        if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.CostCenterId == id, ct))
            throw new ConflictException(Messages.CannotDeleteCostCenterWithEntries);

        string? usage =
            await Db.Set<FixedAsset>().AnyAsync(a => a.CostCenterId == id, ct)
                || await Db.Set<FixedAssetDepreciation>().AnyAsync(d => d.CostCenterId == id, ct) ? Messages.CostCenterUsageFixedAssets
            : await Db.Set<Employee>().AnyAsync(e => e.CostCenterId == id, ct) ? Messages.CostCenterUsageEmployees
            : await Db.Set<PayrollLine>().AnyAsync(p => p.CostCenterId == id, ct) ? Messages.CostCenterUsagePayroll
            : await Db.Set<CommercialContract>().AnyAsync(c => c.CostCenterId == id, ct) ? Messages.CostCenterUsageContracts
            : await Db.Set<InvoiceItemBase>().AnyAsync(i => i.CostCenterId == id, ct)
                || await Db.Set<InvoiceBase>().AnyAsync(i => i.CostCenterId == id, ct) ? Messages.CostCenterUsageInvoices
            : await Db.Set<Voucher>().AnyAsync(v => v.CostCenterId == id, ct) ? Messages.CostCenterUsageVouchers
            : null;
        if (usage != null) throw new ConflictException(string.Format(Messages.CannotDeleteCostCenterInUse, usage));
    }
}
