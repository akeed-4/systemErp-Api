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
            throw new ConflictException("كود مركز التكلفة مستخدم مسبقاً.");
    }

    protected override async Task OnDeletingAsync(CostCenter entity, CancellationToken ct)
    {
        if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.CostCenterId == entity.Id, ct))
            throw new ConflictException("لا يمكن حذف مركز تكلفة عليه حركات.");
    }
}
