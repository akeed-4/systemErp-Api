using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// تذاكر الدعم الفني للمنشأة: الترقيم والحالة وبيانات العميل والرد الآلي من الخادم.
/// التعديل والحذف للتذكرة المفتوحة فقط؛ التذكرة المغلقة لا تقبل ردوداً.
/// </summary>
public class SupportTicketService : CrudService<SupportTicket, SupportTicketDto, CreateSupportTicketDto, UpdateSupportTicketDto>, ISupportTicketService
{
    private static readonly string[] Departments = { "accounting", "inventory", "zatca", "showroom", "technical" };
    private static readonly string[] Priorities = { "low", "medium", "high", "urgent" };
    private static readonly string[] Statuses = { "open", "in_progress", "resolved", "closed" };
    private const string SystemAuthor = "النظام الآلي للدعم الفني";
    private const string AutoReply = "تم استلام تذكرتك بنجاح وسيتم الرد عليك في أقرب وقت ممكن من قبل فريق المتخصصين.";

    private readonly INumberSequenceService _numbers;
    private readonly ICurrentUser _user;

    public SupportTicketService(ErpDbContext db, INumberSequenceService numbers, ICurrentUser user) : base(db)
    {
        _numbers = numbers; _user = user;
    }

    protected override string Label => "تذكرة الدعم";
    protected override bool Transactional => true;

    protected override IQueryable<SupportTicket> ApplySearch(IQueryable<SupportTicket> q, string term)
        => q.Where(t => t.Title.Contains(term) || t.TicketNumber.Contains(term) || t.Description.Contains(term));

    protected override IQueryable<SupportTicket> ApplyFilters(IQueryable<SupportTicket> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(t => t.Status == p.Status);

    protected override Task ValidateAsync(CreateSupportTicketDto d, SupportTicket? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Trim().Length < 5) errors.Add("عنوان التذكرة مطلوب (5 أحرف على الأقل).");
        if (string.IsNullOrWhiteSpace(d.Description) || d.Description.Trim().Length < 10) errors.Add("تفاصيل التذكرة مطلوبة (10 أحرف على الأقل).");
        if (!Departments.Contains(d.Department)) errors.Add("القسم: " + string.Join(" | ", Departments));
        if (!Priorities.Contains(d.Priority)) errors.Add("الأولوية: " + string.Join(" | ", Priorities));
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        if (existing != null && existing.Status != "open") throw new ConflictException("لا تُعدَّل تذكرة بدأت معالجتها أو أُغلقت.");
        return Task.CompletedTask;
    }

    protected override async Task OnCreatingAsync(SupportTicket e, CreateSupportTicketDto d, CancellationToken ct)
    {
        var tenant = await Db.Set<Tenant>().AsNoTracking().FirstOrDefaultAsync(ct);
        e.TicketNumber = await _numbers.NextAsync("support_ticket", "TKT-", ct);
        e.Title = d.Title.Trim();
        e.Description = d.Description.Trim();
        e.Status = "open";
        e.ClientName = tenant?.NameAr ?? string.Empty;
        e.ClientEmail = string.IsNullOrWhiteSpace(tenant?.Email) ? null : tenant!.Email;
        e.CreatedByUserId = _user.UserId;
        e.Replies = new List<SupportTicketReply> { new() { Author = SystemAuthor, IsStaff = true, Message = AutoReply } };
    }

    protected override Task OnDeletingAsync(SupportTicket e, CancellationToken ct)
    {
        if (e.Status != "open") throw new ConflictException("لا تُحذف إلا التذكرة المفتوحة التي لم تبدأ معالجتها.");
        return Task.CompletedTask;
    }

    public async Task<SupportTicketDto> AddReplyAsync(Guid id, AddSupportTicketReplyDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) throw new ValidationFailedException("نص الرد مطلوب.");
        var ticket = await Db.Set<SupportTicket>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException($"{Label} غير موجودة");
        if (ticket.Status == "closed") throw new ConflictException("التذكرة مغلقة ولا تقبل ردوداً؛ افتح تذكرة جديدة.");

        Db.Add(new SupportTicketReply
        {
            SupportTicketId = ticket.Id, Author = _user.Name ?? string.Empty, AuthorUserId = _user.UserId, IsStaff = false, Message = request.Message.Trim(),
        });
        // رد المنشأة على تذكرة محلولة يعيد فتحها للمتابعة
        if (ticket.Status == "resolved") ticket.Status = "open";
        ticket.UpdatedAt = DateTime.UtcNow;
        await SaveAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SupportTicketDto> ChangeStatusAsync(Guid id, ChangeSupportTicketStatusDto request, CancellationToken ct = default)
    {
        if (!Statuses.Contains(request.Status)) throw new ValidationFailedException("الحالة: " + string.Join(" | ", Statuses));
        var ticket = await Db.Set<SupportTicket>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException($"{Label} غير موجودة");
        ticket.Status = request.Status;
        ticket.UpdatedAt = DateTime.UtcNow;
        await SaveAsync(ct);
        return await GetAsync(id, ct);
    }

    protected override SupportTicketDto ToDto(SupportTicket entity)
    {
        var dto = Mapper.Map<SupportTicketDto>(entity);
        dto.Replies = dto.Replies.OrderBy(r => r.CreatedAt).ToList();
        return dto;
    }
}
