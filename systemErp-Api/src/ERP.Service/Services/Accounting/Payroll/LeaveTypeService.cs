using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class LeaveTypeService : CrudService<LeaveType, LeaveTypeDto, CreateLeaveTypeDto, UpdateLeaveTypeDto>, ILeaveTypeService
{
    /// <summary>
    /// الأنواع المعتادة في نظام العمل السعودي، تُنشأ للمنشأة عند أول قراءة وتبقى قابلة للتعديل.
    /// الإجازة المرضية على شريحتين: 30 يوماً بأجر كامل ثم 60 يوماً بثلاثة أرباع الأجر.
    /// </summary>
    private static readonly LeaveType[] Defaults =
    {
        new() { Code = "annual", NameAr = "إجازة سنوية", NameEn = "Annual leave", IsAnnual = true, PayPercent = 100 },
        new() { Code = "sick", NameAr = "إجازة مرضية (أجر كامل)", NameEn = "Sick leave (full pay)", PayPercent = 100, MaxDaysPerYear = 30 },
        new() { Code = "sick_75", NameAr = "إجازة مرضية (75% من الأجر)", NameEn = "Sick leave (75% pay)", PayPercent = 75, MaxDaysPerYear = 60 },
        new() { Code = "unpaid", NameAr = "إجازة بدون أجر", NameEn = "Unpaid leave", PayPercent = 0 },
        new() { Code = "maternity", NameAr = "إجازة وضع", NameEn = "Maternity leave", PayPercent = 100, MaxDaysPerYear = 84 },
        new() { Code = "paternity", NameAr = "إجازة مولود", NameEn = "Paternity leave", PayPercent = 100, MaxDaysPerYear = 3 },
        new() { Code = "marriage", NameAr = "إجازة زواج", NameEn = "Marriage leave", PayPercent = 100, MaxDaysPerYear = 5 },
        new() { Code = "bereavement", NameAr = "إجازة وفاة", NameEn = "Bereavement leave", PayPercent = 100, MaxDaysPerYear = 5 },
        new() { Code = "hajj", NameAr = "إجازة حج", NameEn = "Hajj leave", PayPercent = 100, MaxDaysPerYear = 15 },
    };

    public LeaveTypeService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelLeaveType;

    protected override DevExtreme.AspNet.Data.SortingInfo[] DefaultLoadSort => new[] { EntityLoader.Asc(nameof(LeaveType.Code)) };

    public override async Task<PagedResult<LeaveTypeDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        if (!await Db.Set<LeaveType>().AnyAsync(ct))
        {
            foreach (var t in Defaults)
                Db.Add(new LeaveType { Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn, IsAnnual = t.IsAnnual, PayPercent = t.PayPercent, MaxDaysPerYear = t.MaxDaysPerYear });
            await SaveAsync(ct);
        }
        return await base.ListAsync(p, ct);
    }

    protected override IQueryable<LeaveType> ApplySearch(IQueryable<LeaveType> q, string t)
        => q.Where(x => x.Code.Contains(t) || x.NameAr.Contains(t) || (x.NameEn != null && x.NameEn.Contains(t)));

    protected override async Task ValidateAsync(CreateLeaveTypeDto d, LeaveType? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Code) || string.IsNullOrWhiteSpace(d.NameAr))
            throw new ValidationFailedException(Messages.CodeAndArabicNameRequired);
        if (d.PayPercent is < 0 or > 100) throw new ValidationFailedException(Messages.LeaveTypePayPercentInvalid);
        if (d.MaxDaysPerYear is <= 0) throw new ValidationFailedException(Messages.LeaveTypeMaxDaysInvalid);
        // الإجازة السنوية مدفوعة بالكامل ورصيدها هو حدّها
        if (d.IsAnnual && d.PayPercent != 100) throw new ValidationFailedException(Messages.LeaveTypeAnnualMustBePaid);
        if (await Db.Set<LeaveType>().AnyAsync(x => x.Code == d.Code && (existing == null || x.Id != existing.Id), ct))
            throw new ConflictException(Messages.LeaveTypeCodeInUse);
    }

    protected override async Task OnDeletingAsync(LeaveType entity, CancellationToken ct)
    {
        if (await Db.Set<LeaveRequest>().AnyAsync(r => r.LeaveTypeId == entity.Id, ct))
            throw new ConflictException(Messages.LeaveTypeHasRequests);
    }
}
