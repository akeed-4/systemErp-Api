using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class CostCenterService : CrudService<CostCenter, CostCenterDto, CreateCostCenterDto, UpdateCostCenterDto>, ICostCenterService
{
    public CostCenterService(ErpDbContext db) : base(db) { }
    protected override string Label => "مركز التكلفة";

    protected override IQueryable<CostCenter> ApplySearch(IQueryable<CostCenter> q, string t)
        => q.Where(c => c.Code.Contains(t) || c.NameAr.Contains(t) || c.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateCostCenterDto dto, CostCenter? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.NameAr))
            throw new ValidationFailedException("الكود والاسم بالعربية مطلوبان.");
        if (await Db.Set<CostCenter>().AnyAsync(c => c.Code == dto.Code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException("كود مركز التكلفة مستخدم مسبقاً.");        if (dto.ParentCostCenterId.HasValue)
        {
            if (existing?.Id == dto.ParentCostCenterId.Value)
                throw new ValidationFailedException("A cost center cannot be its own parent.");
            var parent = await Db.Set<CostCenter>().AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == dto.ParentCostCenterId.Value && c.IsActive, ct)
                ?? throw new ValidationFailedException("Parent cost center does not exist or is inactive.");
            var visited = new HashSet<Guid>();
            while (parent.ParentCostCenterId.HasValue && visited.Add(parent.Id))
            {
                if (existing?.Id == parent.ParentCostCenterId.Value)
                    throw new ValidationFailedException("A cost center cannot be moved below one of its descendants.");
                var nextId = parent.ParentCostCenterId.Value;
                var next = await Db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == nextId, ct);
                if (next == null) break;
                parent = next;
            }
        }
    }

    protected override async Task OnDeletingAsync(CostCenter entity, CancellationToken ct)
    {
        if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.CostCenterId == entity.Id, ct)
            || await Db.Set<CommercialContract>().AnyAsync(c => c.CostCenterId == entity.Id, ct)
            || await Db.Set<InvoiceItem>().AnyAsync(i => i.CostCenterId == entity.Id, ct)
            || await Db.Set<CostCenter>().AnyAsync(c => c.ParentCostCenterId == entity.Id, ct))
            throw new ConflictException("لا يمكن حذف مركز تكلفة عليه حركات.");
    }
}
