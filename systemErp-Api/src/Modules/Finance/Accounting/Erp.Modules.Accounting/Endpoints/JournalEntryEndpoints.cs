using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Application;
using Erp.Modules.Accounting.Domain;
using Erp.Modules.Accounting.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Accounting.Endpoints;

internal sealed record JournalLineRequest(Guid? AccountId, string? AccountCode, decimal Debit, decimal Credit, Guid? CostCenterId, string? Notes);

/// <param name="Post">true = create and post in one call (the frontend's behaviour); false = keep as draft.</param>
internal sealed record SaveJournalEntryRequest(DateOnly Date, string Description, string? ReferenceNumber, IReadOnlyList<JournalLineRequest> Lines, bool? Post);

internal sealed record ReverseRequest(DateOnly? Date, string? Reason);

internal sealed record JournalLineDto(Guid Id, int LineNo, Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit, Guid? CostCenterId, string? Notes);

/// <summary>Frontend JournalEntry shape (referenceType = source document type, sourceType = source module).</summary>
internal sealed record JournalEntryDto(
    Guid Id,
    Guid TenantId,
    string? EntryNumber,
    DateOnly Date,
    string Description,
    string ReferenceType,
    Guid ReferenceId,
    string? ReferenceNumber,
    string SourceType,
    string PostingKind,
    JournalEntryStatus Status,
    decimal TotalDebit,
    decimal TotalCredit,
    bool IsBalanced,
    Guid? ReversalOfId,
    Guid? ReversedById,
    DateTimeOffset CreatedAt,
    IReadOnlyList<JournalLineDto> Lines);

/// <summary>Journal entries (accounts-tree screen). Manual entries are drafts until posted; posted entries are only reversed.</summary>
internal static class JournalEntryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var entries = app.MapGroup("/api/v1/journal-entries").WithTags("Accounting");
        entries.MapGet(string.Empty, ListAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.View);
        entries.MapGet("{id:guid}", GetAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.View);
        entries.MapPost(string.Empty, CreateAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Create);
        entries.MapPut("{id:guid}", UpdateAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Edit);
        entries.MapDelete("{id:guid}", DeleteAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Delete);
        entries.MapPost("{id:guid}/post", PostAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Approve);
        entries.MapPost("{id:guid}/reverse", ReverseAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Approve);
    }

    private static async Task<IResult> ListAsync([AsParameters] PaginationParams paging, string? source, AccountingDbContext db, CancellationToken ct)
    {
        var query = db.JournalEntries.AsNoTracking();
        if (paging.StartDate is { } start)
        {
            query = query.Where(e => e.Date >= start);
        }

        if (paging.EndDate is { } end)
        {
            query = query.Where(e => e.Date <= end);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(e => e.SourceModule == source);
        }

        if (!string.IsNullOrWhiteSpace(paging.SearchTerm))
        {
            var term = paging.SearchTerm.Trim();
            query = query.Where(e => e.Description.Contains(term) || (e.EntryNumber != null && e.EntryNumber.Contains(term)) || (e.SourceDocumentNumber != null && e.SourceDocumentNumber.Contains(term)));
        }

        var page = await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ToPagedResultAsync(paging, ct);
        var ids = page.Items.Select(e => e.Id).ToList();
        var lines = await db.JournalEntryLines.AsNoTracking().Where(l => ids.Contains(l.JournalEntryId)).ToListAsync(ct);
        var dtos = await ToDtosAsync(db, page.Items, lines, ct);
        return ErpResults.Ok(new PagedResult<JournalEntryDto>(dtos, page.TotalCount, page.PageNumber, page.PageSize));
    }

    private static async Task<IResult> GetAsync(Guid id, AccountingDbContext db, CancellationToken ct) =>
        ErpResults.Ok(await LoadDtoAsync(db, id, ct));

    private static async Task<IResult> CreateAsync(SaveJournalEntryRequest request, AccountingDbContext db, PostingEngine engine, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var entry = new JournalEntry(request.Date, request.Description, source: null, JournalEntry.ManualKind);
        entry.ReplaceDraft(request.Date, request.Description, request.ReferenceNumber, await BuildLinesAsync(db, request, ct));
        db.JournalEntries.Add(entry);

        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                if (request.Post ?? true)
                {
                    await engine.PostAsync(entry, innerCt);
                }
            },
            ct);
        return ErpResults.Created($"/api/v1/journal-entries/{entry.Id}", await LoadDtoAsync(db, entry.Id, ct), "تم حفظ القيد");
    }

    private static async Task<IResult> UpdateAsync(Guid id, SaveJournalEntryRequest request, AccountingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();
        EnsureManual(entry);
        var lines = await BuildLinesAsync(db, request, ct);
        db.JournalEntryLines.RemoveRange(entry.Lines);
        entry.ReplaceDraft(request.Date, request.Description, request.ReferenceNumber, lines);
        db.JournalEntryLines.AddRange(entry.Lines);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(await LoadDtoAsync(db, id, ct), "تم تحديث القيد");
    }

    private static async Task<IResult> DeleteAsync(Guid id, AccountingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();
        EnsureManual(entry);
        entry.EnsureDraft();
        db.JournalEntries.Remove(entry);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(new { id }, "تم حذف القيد");
    }

    private static async Task<IResult> PostAsync(Guid id, AccountingDbContext db, PostingEngine engine, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();
        await unitOfWork.ExecuteAsync(innerCt => engine.PostAsync(entry, innerCt), ct);
        return ErpResults.Ok(await LoadDtoAsync(db, id, ct), "تم ترحيل القيد");
    }

    private static async Task<IResult> ReverseAsync(
        Guid id,
        ReverseRequest? request,
        AccountingDbContext db,
        AccountingPostingService posting,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct)
    {
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();
        var date = request?.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var reversal = await unitOfWork.ExecuteAsync(innerCt => posting.ReverseEntryAsync(entry, date, request?.Reason ?? "عكس يدوي", innerCt), ct);
        return ErpResults.Ok(await LoadDtoAsync(db, reversal.Id, ct), "تم إنشاء القيد العكسي");
    }

    private static async Task<List<JournalEntryLine>> BuildLinesAsync(AccountingDbContext db, SaveJournalEntryRequest request, CancellationToken ct)
    {
        if (request.Lines is null || request.Lines.Count == 0 || string.IsNullOrWhiteSpace(request.Description))
        {
            throw ErpException.Validation("A description and lines are required.", "البيان وأسطر القيد مطلوبة.");
        }

        var codes = request.Lines.Where(l => l.AccountId is null).Select(l => l.AccountCode?.Trim()).OfType<string>().Distinct().ToList();
        var byCode = await db.Accounts.AsNoTracking().Where(a => codes.Contains(a.Code)).ToDictionaryAsync(a => a.Code, a => a.Id, ct);

        return request.Lines.Select(l =>
        {
            var accountId = l.AccountId ?? (l.AccountCode is { } code && byCode.TryGetValue(code.Trim(), out var id)
                ? id
                : throw ErpException.Validation($"Unknown account '{l.AccountCode}'.", $"الحساب '{l.AccountCode}' غير موجود."));
            return new JournalEntryLine(accountId, l.Debit, l.Credit, l.CostCenterId, null, null, l.Notes);
        }).ToList();
    }

    private static void EnsureManual(JournalEntry entry)
    {
        if (!entry.IsManual)
        {
            throw ErpException.Conflict("entry_not_manual", "Entries created by other documents are changed through those documents.", "القيود الآلية تُعدَّل من المستند المصدر فقط.");
        }
    }

    private static async Task<JournalEntryDto> LoadDtoAsync(AccountingDbContext db, Guid id, CancellationToken ct)
    {
        var entry = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();
        var lines = await db.JournalEntryLines.AsNoTracking().Where(l => l.JournalEntryId == id).ToListAsync(ct);
        return (await ToDtosAsync(db, [entry], lines, ct))[0];
    }

    private static async Task<List<JournalEntryDto>> ToDtosAsync(AccountingDbContext db, IReadOnlyList<JournalEntry> entries, List<JournalEntryLine> lines, CancellationToken ct)
    {
        var accountIds = lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return entries.Select(e => new JournalEntryDto(
            e.Id,
            e.TenantId,
            e.EntryNumber,
            e.Date,
            e.Description,
            e.SourceDocumentType,
            e.SourceDocumentId,
            e.SourceDocumentNumber,
            e.SourceModule,
            e.PostingKind,
            e.Status,
            e.TotalDebit,
            e.TotalCredit,
            e.TotalDebit == e.TotalCredit,
            e.ReversalOfId,
            e.ReversedById,
            e.CreatedAt,
            lines.Where(l => l.JournalEntryId == e.Id).OrderBy(l => l.LineNo)
                .Select(l => new JournalLineDto(l.Id, l.LineNo, l.AccountId, accounts[l.AccountId].Code, accounts[l.AccountId].NameAr, l.Debit, l.Credit, l.CostCenterId, l.Notes))
                .ToList())).ToList();
    }

    private static ErpException NotFound() => ErpException.NotFound("Journal entry", "القيد");
}
