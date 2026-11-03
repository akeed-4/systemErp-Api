using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ERP.Service.Services.Shared;

/// <summary>
/// بوابة الاعتماد للعمليات المحجوزة (انظر IApprovalGate). العملية تُجرَّب داخل معاملة تُلغى دائماً — النمط نفسه في معاينة قيد الفاتورة —
/// فتُرفض البيانات غير الصحيحة قبل وصولها للمعتمِد، ويُبنى ملخصه من نتيجتها الفعلية دون أي أثر محفوظ أو رقم مستهلك.
/// عند الاعتماد النهائي تُنفَّذ العملية بخدمة المستند نفسها وبياناتها المحفوظة، ويُنسب المستند الناتج لمقدّم الطلب.
/// </summary>
public class ApprovalGate : IApprovalGate
{
    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ErpDbContext _db;
    private readonly IApprovalService _approvals;
    private readonly IServiceProvider _services;

    public ApprovalGate(ErpDbContext db, IApprovalService approvals, IServiceProvider services)
    {
        _db = db; _approvals = approvals; _services = services;
    }

    public async Task<ApprovalRequestDto?> TryHoldAsync(string handler, string action, Guid? documentId, object? payload, CancellationToken ct = default)
    {
        var h = ApprovalRegistry.Find(handler);
        if (h == null || !h.Operations.TryGetValue(action, out var op)) return null;
        if (op.RequiresId && documentId == null) return null;

        // نوع مستند السياسة قد يتحدد من البيانات (سند قبض/صرف، أمر بيع/شراء) أو من المستند الحالي
        object? current = documentId.HasValue && h.Load != null ? await h.Load(_services, documentId.Value, ct) : null;
        var documentType = h.ResolveType(payload, current);
        if (!ApprovalDocumentCatalog.Supports(documentType, action)) return null;
        if (!await _approvals.HasActivePolicyAsync(documentType, action, ct)) return null;

        if (documentId.HasValue && await _db.Set<ApprovalRequest>().AnyAsync(r => r.DocumentId == documentId && r.Status == "pending", ct))
            throw new ConflictException(Messages.ApprovalPendingOnDocument);

        var result = op.DryRun ? await DryRunAsync(op, documentId, payload, ct) : null;
        var source = result ?? current ?? payload;
        // رقم المستند الجديد يُصدر عند التنفيذ الفعلي (رقم التجربة قد يتغير)، فلا يُعرض قبل ذلك
        var isNew = documentId == null;
        var snapshot = ApprovalSnapshotBuilder.Build(documentType, action, source, h.Amount, includeNumber: !isNew);
        var definition = ApprovalDocumentCatalog.Find(documentType)!;

        return await _approvals.HoldAsync(new ApprovalCheckRequestDto
        {
            DocumentType = documentType, ActionType = action, DocumentId = documentId ?? Guid.Empty,
            DocumentNumber = isNew ? $"{definition.NameAr} (جديد)" : ApprovalSnapshotBuilder.Number(current) ?? snapshot.Number ?? string.Empty,
            DocumentAmount = snapshot.Amount,
            DocumentDataSnapshot = JsonSerializer.Serialize(snapshot, SnapshotJson),
        }, handler, payload == null ? "{}" : JsonSerializer.Serialize(payload, payload.GetType(), PayloadJson), ct);
    }

    /// <summary>تنفيذ عملية محجوزة بعد اعتمادها النهائي (داخل معاملة القرار)؛ يعيد معرّف المستند الناتج ورقمه.</summary>
    public async Task<(Guid DocumentId, string? DocumentNumber)> ExecuteAsync(ApprovalDeferredOperation deferred, CancellationToken ct = default)
    {
        var h = ApprovalRegistry.Find(deferred.Handler) ?? throw new ConflictException(Messages.ApprovalHandlerMissing);
        if (!h.Operations.TryGetValue(deferred.Action, out var op)) throw new ConflictException(Messages.ApprovalHandlerMissing);
        var payload = op.PayloadType == null || string.IsNullOrWhiteSpace(deferred.Payload)
            ? null
            : JsonSerializer.Deserialize(deferred.Payload, op.PayloadType, PayloadJson);
        var documentId = deferred.DocumentId == Guid.Empty ? (Guid?)null : deferred.DocumentId;

        // المستند المنشأ يُنسب لمقدّم الطلب لا للمعتمِد (نطاق «السجلات التي أنشأها»)
        void StampRequester(object? sender, SavingChangesEventArgs e)
        {
            foreach (var entry in _db.ChangeTracker.Entries<IScopedRecord>().Where(x => x.State == EntityState.Added))
                entry.Entity.CreatedByUserId = deferred.RequesterUserId;
        }
        _db.SavingChanges += StampRequester;
        try
        {
            var result = await op.Run(_services, documentId, payload, ct);
            return (ApprovalSnapshotBuilder.Id(result) ?? documentId ?? Guid.Empty, ApprovalSnapshotBuilder.Number(result));
        }
        finally
        {
            _db.SavingChanges -= StampRequester;
        }
    }

    public ApprovalDocumentCatalogDto Catalog() => new()
    {
        Actions = ApprovalDocumentCatalog.ActionDefinitions.Select(a => new ApprovalActionDto { Key = a.Key, NameAr = a.NameAr, NameEn = a.NameEn }).ToList(),
        Modules = ApprovalDocumentCatalog.Modules.Select(m => new ApprovalModuleDto
        {
            Key = m.Key, NameAr = m.NameAr, NameEn = m.NameEn,
            Documents = ApprovalDocumentCatalog.Documents.Where(d => d.Module == m.Key).Select(d => new ApprovalDocumentTypeDto
            {
                Key = d.Key, Module = d.Module, NameAr = d.NameAr, NameEn = d.NameEn, Actions = d.Actions.ToList(),
            }).ToList(),
        }).Where(m => m.Documents.Count > 0).ToList(),
    };

    /// <summary>تجربة العملية في معاملة تُلغى دائماً؛ أخطاء التحقق تصل للمستخدم كما لو نُفِّذت.</summary>
    private async Task<object?> DryRunAsync(ApprovalOperation op, Guid? documentId, object? payload, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction != null) return null; // لا تُعزل التجربة داخل معاملة قائمة
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            return await op.Run(_services, documentId, payload, ct);
        }
        finally
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
        }
    }
}
