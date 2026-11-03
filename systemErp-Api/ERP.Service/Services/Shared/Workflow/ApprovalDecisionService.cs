using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// قرار المعتمِد وأثره على المستند: الجرد يمرّ بمسار اعتماده (تسوية الفروق وقيدها)، والفواتير ترحَّل عند الاعتماد
/// النهائي في معاملة القرار نفسها — فإن تعذّر الترحيل (رصيد، فترة مقفلة...) لا يُسجَّل الاعتماد ويظهر السبب للمعتمِد.
/// الإرسال للهيئة بعد اكتمال المعاملة بقواعد الإرسال التلقائي نفسها.
/// </summary>
public class ApprovalDecisionService : IApprovalDecisionService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IApprovalService _approvals;
    private readonly IInventoryCountApprovalService _counts;
    private readonly TradeInvoiceEngine _engine;
    private readonly IAuditService _audit;

    public ApprovalDecisionService(ErpDbContext db, ITransactionRunner tx, IApprovalService approvals,
        IInventoryCountApprovalService counts, TradeInvoiceEngine engine, IAuditService audit)
    {
        _db = db; _tx = tx; _approvals = approvals; _counts = counts; _engine = engine; _audit = audit;
    }

    /// <summary>ترحيل مسودة فاتورة في جدولها، وإرسالها للهيئة بعد المعاملة.</summary>
    private sealed record DraftInvoice(Func<CancellationToken, Task> Post, Func<CancellationToken, Task> Submit);

    public async Task<ApprovalRequestDto> ApproveAsync(Guid id, ApprovalDecisionDto d, CancellationToken ct = default)
    {
        var request = await _approvals.GetAsync(id, ct);
        if (request.DocumentType == InventoryCountService.DocumentType)
        {
            await _counts.ApproveAsync(request.DocumentId, d, ct);
            return await _approvals.GetAsync(id, ct);
        }

        var draft = await DraftInvoiceAsync(request, ct);
        var (saved, posted) = await _tx.RunAsync(async token =>
        {
            var result = await _approvals.ApproveAsync(id, d, token);
            if (result.Status != "approved" || draft == null) return (result, false);
            await draft.Post(token);
            return (result, true);
        }, ct);
        if (posted) await draft!.Submit(ct);
        return saved;
    }

    public async Task<ApprovalRequestDto> RejectAsync(Guid id, ApprovalDecisionDto d, CancellationToken ct = default)
    {
        var request = await _approvals.GetAsync(id, ct);
        if (request.DocumentType == InventoryCountService.DocumentType)
        {
            await _counts.RejectAsync(request.DocumentId, d, ct);
            return await _approvals.GetAsync(id, ct);
        }

        // الفاتورة تبقى مسودة مرفوضة لا تُرحَّل؛ يُشعَر مقدّمها بالسبب، وتعديلها وحفظها للترحيل يعيد إرسالها للاعتماد
        var saved = await _approvals.RejectAsync(id, d, ct);
        await _audit.LogAsync("APPROVAL_REJECTED", request.DocumentType, request.DocumentId.ToString(),
            $"رفض اعتماد {request.DocumentNumber}: {d.Comment}", ct);
        return saved;
    }

    /// <summary>مسودة الفاتورة المرتبطة بالطلب في جدول نوعها (أو جدول المعارض)؛ null لغير الفواتير أو لما لم يعد مسودة.</summary>
    private async Task<DraftInvoice?> DraftInvoiceAsync(ApprovalRequestDto r, CancellationToken ct) => r.DocumentType switch
    {
        "sales" => await FindAsync<SalesInvoice>(r.DocumentId, ct) ?? await FindAsync<Invoice>(r.DocumentId, ct),
        "purchase" => await FindAsync<PurchaseInvoice>(r.DocumentId, ct) ?? await FindAsync<Invoice>(r.DocumentId, ct),
        "sales_return" => await FindAsync<SalesReturn>(r.DocumentId, ct) ?? await FindAsync<Invoice>(r.DocumentId, ct),
        "purchase_return" => await FindAsync<PurchaseReturn>(r.DocumentId, ct) ?? await FindAsync<Invoice>(r.DocumentId, ct),
        _ => null,
    };

    private async Task<DraftInvoice?> FindAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
        => await _db.Set<T>().AnyAsync(i => i.Id == id && i.Status == "draft", ct)
            ? new DraftInvoice(token => _engine.PostDraftAsync<T>(id, token), token => _engine.SubmitIfRequestedAsync<T>(id, token))
            : null;
}
