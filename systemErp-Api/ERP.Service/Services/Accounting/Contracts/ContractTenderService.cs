using System.Text.Json;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using T = ERP.Core.Models.Accounting.ContractTenderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// المناقصة هي مسودة عقد المقاولات نفسه (جدول الكميات وأسعار العطاء وخصم التفاوض والتكلفة التقديرية عليه)، فالترسية لا تعيد إدخال شيء:
/// العقد يتقدم بعدها في دورته العادية (مراجعة ← اعتماد ← سريان). القواعد:
/// <list type="bullet">
/// <item>الانتقالات من <see cref="ContractTenderStatuses.CanMove"/>، وفي مسودة العقد وحدها.</item>
/// <item>التقديم يحفظ نسخة كاملة من العرض (<see cref="ContractTenderRevision"/>)؛ والعودة للتسعير ثم إعادة التقديم نسخة جديدة.</item>
/// <item>الترسية تنشئ ملف المشروع ومركز تكلفته (لا تُنشأ للمناقصة قبلها)، والخسارة بسبب إلزامي.</item>
/// </list>
/// </summary>
public class ContractTenderService : IContractTenderService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;

    public ContractTenderService(ErpDbContext db, ICurrentUser user)
    {
        _db = db; _user = user;
    }

    public async Task<List<TenderRowDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await RowsAsync(null, ct);
        // المفتوحة أولاً بأقرب موعد، ثم المحسومة الأحدث
        return rows.OrderBy(r => T.IsOpen(r.Status) ? 0 : 1)
            .ThenBy(r => T.IsOpen(r.Status) ? r.Deadline ?? DateTime.MaxValue : DateTime.MaxValue)
            .ThenByDescending(r => r.DecidedAt ?? r.CreatedAt).ToList();
    }

    public async Task<TenderRowDto> GetAsync(Guid contractId, CancellationToken ct = default)
        => (await RowsAsync(contractId, ct)).FirstOrDefault() ?? throw new NotFoundException("المناقصة غير موجودة.");

    public async Task<List<TenderRevisionDto>> RevisionsAsync(Guid contractId, CancellationToken ct = default)
    {
        var list = await _db.Set<ContractTenderRevision>().AsNoTracking().Where(r => r.ContractId == contractId)
            .OrderByDescending(r => r.RevisionNo).ToListAsync(ct);
        return list.Select(r => new TenderRevisionDto
        {
            Id = r.Id, RevisionNo = r.RevisionNo, SubmittedAt = r.SubmittedAt, SubmittedBy = r.SubmittedBy, TenderValue = r.TenderValue,
            DiscountPercent = r.DiscountPercent, ContractValue = r.ContractValue, VatAmount = r.VatAmount, EstimatedCost = r.EstimatedCost,
            Note = r.Note, Lines = JsonSerializer.Deserialize<List<TenderRevisionLineDto>>(r.LinesJson, Json) ?? new(),
        }).ToList();
    }

    public Task<TenderRowDto> MoveAsync(Guid contractId, MoveTenderDto request, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var c = await _db.Set<CommercialContract>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == contractId, token)
                ?? throw new NotFoundException(Messages.ContractNotFound);
            if (c.TenderStatus == null) throw new ConflictException("العقد ليس مناقصة.");
            if (c.Stage != ContractStageDefinition.Draft) throw new ConflictException("مسار المناقصة في مسودة العقد وحدها.");
            var to = (request.Status ?? string.Empty).Trim();
            if (!T.IsValid(to) || !T.CanMove(c.TenderStatus, to))
                throw new ConflictException($"لا يُنقل العطاء من «{c.TenderStatus}» إلى «{to}».");
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

            switch (to)
            {
                case T.Submitted:
                    if (c.Items.Count == 0 || c.ContractValue <= 0) throw new ValidationFailedException("العرض بلا بنود أو بلا قيمة: سعّر جدول الكميات قبل التقديم.");
                    await SnapshotAsync(c, note, token);
                    break;
                case T.Lost:
                    if (note == null) throw new ValidationFailedException("سبب الخسارة أو الانسحاب مطلوب.");
                    c.TenderDecidedAt = DateTime.UtcNow; c.TenderResultNote = note;
                    break;
                case T.Awarded:
                    c.TenderDecidedAt = DateTime.UtcNow; c.TenderResultNote = note;
                    // المشروع يبدأ بالترسية: ملفه ومركز تكلفته (العقد في مسودته يكمل دورته بعدها)
                    if (!await _db.Set<ConstructionProjectProfile>().AnyAsync(p => p.ContractId == c.Id, token))
                        await ConstructionProjectService.CreateForContractAsync(_db, c, token);
                    break;
            }
            c.TenderStatus = to;
            await _db.SaveChangesAsync(token);
            return (await RowsAsync(c.Id, token)).First();
        }, ct);

    /// <summary>نسخة العرض المقدَّم: القيم وأسطر جدول الكميات كما هي لحظة التقديم.</summary>
    private async Task SnapshotAsync(CommercialContract c, string? note, CancellationToken ct)
    {
        var next = (await _db.Set<ContractTenderRevision>().Where(r => r.ContractId == c.Id).MaxAsync(r => (int?)r.RevisionNo, ct) ?? 0) + 1;
        var lines = c.Items.OrderBy(i => i.SortOrder).Select(i => new TenderRevisionLineDto
        {
            LineCode = i.LineCode, SectionName = i.SectionName, Description = i.Description, Unit = i.UnitDescription ?? i.Unit,
            Quantity = i.Quantity, TenderUnitPrice = i.TenderUnitPrice, UnitPrice = i.UnitPrice, EstimatedUnitCost = i.EstimatedUnitCost,
            IsSubItem = i.IsSubItem, IsGroup = i.IsGroup,
        }).ToList();
        _db.Add(new ContractTenderRevision
        {
            ContractId = c.Id, RevisionNo = next, SubmittedAt = DateTime.UtcNow, SubmittedBy = _user.Name ?? "system",
            TenderValue = c.TenderValue, DiscountPercent = c.DiscountPercent, ContractValue = c.ContractValue, VatAmount = c.VatAmount,
            EstimatedCost = c.EstimatedCost, Note = note, LinesJson = JsonSerializer.Serialize(lines, Json),
        });
    }

    private async Task<List<TenderRowDto>> RowsAsync(Guid? onlyId, CancellationToken ct)
    {
        var contracts = await _db.Set<CommercialContract>().AsNoTracking()
            .Where(c => c.TenderStatus != null && (onlyId == null || c.Id == onlyId))
            .Select(c => new
            {
                c.Id, c.ContractNumber, c.TenderNumber, c.Title, c.ProjectName, c.ProjectLocation, c.PartyName, c.TenderStatus, c.Stage,
                c.TenderIssueDate, c.TenderDeadline, c.DurationMonths, c.TenderValue, c.DiscountPercent, c.ContractValue, c.EstimatedCost,
                c.TenderDecidedAt, c.TenderResultNote, c.TenderRequirements, c.CreatedAt,
                ItemCount = c.Items.Count(i => !i.IsGroup),
            }).ToListAsync(ct);
        var ids = contracts.Select(c => c.Id).ToList();
        var revisions = await _db.Set<ContractTenderRevision>().AsNoTracking().Where(r => ids.Contains(r.ContractId))
            .GroupBy(r => r.ContractId).Select(g => new { g.Key, Count = g.Count(), Last = g.Max(r => r.SubmittedAt) })
            .ToDictionaryAsync(x => x.Key, ct);
        var today = SaudiTime.Today;
        return contracts.Select(c =>
        {
            var rev = revisions.GetValueOrDefault(c.Id);
            var beforeSubmit = c.TenderStatus is T.New or T.Studying or T.Pricing;
            return new TenderRowDto
            {
                Id = c.Id, ContractNumber = c.ContractNumber, TenderNumber = c.TenderNumber, Title = c.Title, ProjectName = c.ProjectName,
                ProjectLocation = c.ProjectLocation, PartyName = c.PartyName, Status = c.TenderStatus!, Stage = c.Stage,
                IssueDate = c.TenderIssueDate, Deadline = c.TenderDeadline,
                DaysToDeadline = beforeSubmit && c.TenderDeadline is DateTime d ? (int)(d.Date - today).TotalDays : null,
                DurationMonths = c.DurationMonths, TenderValue = c.TenderValue, DiscountPercent = c.DiscountPercent, ContractValue = c.ContractValue,
                EstimatedCost = c.EstimatedCost,
                MarginPercent = c.EstimatedCost > 0 && c.ContractValue > 0 ? Math.Round((c.ContractValue - c.EstimatedCost) / c.ContractValue * 100m, 2) : null,
                ItemCount = c.ItemCount, RevisionCount = rev?.Count ?? 0, LastSubmittedAt = rev?.Last,
                DecidedAt = c.TenderDecidedAt, ResultNote = c.TenderResultNote, Requirements = c.TenderRequirements, CreatedAt = c.CreatedAt,
            };
        }).ToList();
    }
}
