using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// فروع النشاط. الفرع وحدة إدارية بلا حسابات ولا مخزون، فلا يدخل في حد مواقع الاشتراك.
/// القواعد: الرمز فريد في النشاط، فرع افتراضي واحد دائماً، ولا يُوقف فرع له مواقع فعّالة، ولا يُحذف فرع له مواقع.
/// </summary>
public class BranchService : CrudService<Branch, BranchDto, CreateBranchDto, UpdateBranchDto>, IBranchService
{
    private static readonly string[] Statuses = { "active", "inactive" };

    public BranchService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelBranch;
    protected override bool Transactional => true;

    protected override IQueryable<Branch> ApplySearch(IQueryable<Branch> q, string t)
        => q.Where(b => b.Code.Contains(t) || b.NameAr.Contains(t) || b.NameEn.Contains(t) || (b.City != null && b.City.Contains(t)));

    protected override async Task ValidateAsync(CreateBranchDto d, Branch? existing, CancellationToken ct)
    {
        d.Code = d.Code?.Trim() ?? string.Empty;
        d.NameAr = d.NameAr?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(d.Code) || string.IsNullOrWhiteSpace(d.NameAr))
            throw new ValidationFailedException(Messages.CodeAndArabicNameRequired);
        d.Status = string.IsNullOrWhiteSpace(d.Status) ? "active" : d.Status.Trim().ToLowerInvariant();
        if (!Statuses.Contains(d.Status)) throw new ValidationFailedException(Messages.BranchStatusInvalid);
        if (await Db.Set<Branch>().AnyAsync(b => b.Code == d.Code && (existing == null || b.Id != existing.Id), ct))
            throw new ConflictException(Messages.BranchCodeInUse);

        if (existing is { IsDefault: true } && !d.IsDefault)
            throw new ConflictException(Messages.SetAnotherDefaultBranch);
        if (d.Status != "active" && (d.IsDefault || existing is { IsDefault: true }))
            throw new ConflictException(Messages.DefaultBranchMustBeActive);
        // إيقاف فرع: مواقعه الفعّالة تُنقل أو توقف أولاً (فلا يبقى موقع فعّال تحت فرع موقوف)
        if (existing != null && d.Status != "active" && existing.Status == "active"
            && await Db.Set<Warehouse>().AnyAsync(w => w.BranchId == existing.Id && w.Status == "active", ct))
            throw new ConflictException(Messages.BranchHasActiveLocations);
    }

    protected override async Task OnCreatingAsync(Branch e, CreateBranchDto d, CancellationToken ct)
    {
        if (!await Db.Set<Branch>().AnyAsync(ct)) e.IsDefault = true; // أول فرع هو الافتراضي
        if (e.IsDefault) await ClearOtherDefaultsAsync(e.Id, ct);
    }

    protected override async Task OnUpdatingAsync(Branch e, UpdateBranchDto d, CancellationToken ct)
    {
        if (e.IsDefault) await ClearOtherDefaultsAsync(e.Id, ct);
    }

    protected override async Task OnDeletingAsync(Branch e, CancellationToken ct)
    {
        if (e.IsDefault) throw new ConflictException(Messages.CannotDeleteDefaultBranch);
        if (await Db.Set<Warehouse>().AnyAsync(w => w.BranchId == e.Id, ct))
            throw new ConflictException(Messages.BranchHasLocations);
    }

    private async Task ClearOtherDefaultsAsync(Guid keep, CancellationToken ct)
    {
        foreach (var b in await Db.Set<Branch>().Where(b => b.IsDefault && b.Id != keep).ToListAsync(ct)) b.IsDefault = false;
    }
}
