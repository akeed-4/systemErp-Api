using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;

namespace ERP.Service.Services.Accounting;

/// <summary>خدمة مستند تجارة عامة على جدوله <typeparamref name="T"/>: نوع ثابت، والمنطق كله في TradeInvoiceEngine.</summary>
public abstract class TradeDocumentService<T> : ITradeDocumentService where T : InvoiceBase, new()
{
    protected readonly TradeInvoiceEngine Engine;

    protected TradeDocumentService(TradeInvoiceEngine engine) => Engine = engine;

    public InvoiceKind Kind { get; } = TradeInvoiceEngine.KindOf<T>()!.Value;

    public Task<PagedResult<InvoiceDto>> ListAsync(PaginationParams query, CancellationToken ct = default) => Engine.ListAsync<T>(null, query, null, ct);
    public Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default) => Engine.LoadAsync<T>(null, options, null, ct);
    public Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default) => Engine.GetAsync<T>(id, ct);

    public Task<InvoiceDto> CreateAsync(CreateInvoiceDto request, CancellationToken ct = default)
    {
        request.Kind = Kind;
        return Engine.CreateAsync<T>(request, ct);
    }

    public Task<InvoiceDto> UpdateAsync(Guid id, UpdateInvoiceDto request, CancellationToken ct = default)
    {
        request.Kind = Kind;
        return Engine.UpdateAsync<T>(id, request, ct);
    }

    public Task<InvoiceDto> PostDraftAsync(Guid id, CancellationToken ct = default) => Engine.PostDraftAsync<T>(id, ct);
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => Engine.DeleteAsync<T>(id, ct);

    public Task<InvoiceJournalDto> PreviewJournalAsync(CreateInvoiceDto request, CancellationToken ct = default)
    {
        request.Kind = Kind;
        return Engine.PreviewJournalAsync<T>(request, ct);
    }

    public Task<InvoiceJournalDto> GetJournalAsync(Guid id, CancellationToken ct = default) => Engine.GetJournalAsync<T>(id, ct);
    public Task<ZatcaSubmitResultDto> SubmitToZatcaAsync(Guid id, CancellationToken ct = default) => Engine.SubmitToZatcaAsync<T>(id, ct);
}

/// <summary>مرتجع على جدول <typeparamref name="T"/> لأصل من جدول <typeparamref name="TOriginal"/>.</summary>
public abstract class TradeReturnService<T, TOriginal> : TradeDocumentService<T>, ITradeReturnService
    where T : InvoiceBase, new() where TOriginal : InvoiceBase, new()
{
    protected TradeReturnService(TradeInvoiceEngine engine) : base(engine) { }

    public Task<InvoiceDto> CreateFromInvoiceAsync(CreateReturnInvoiceRequestDto request, CancellationToken ct = default)
        => Engine.CreateReturnAsync<TOriginal, T>(request, ct);
}

public class SalesInvoiceService : TradeDocumentService<SalesInvoice>, ISalesInvoiceService
{
    public SalesInvoiceService(TradeInvoiceEngine engine) : base(engine) { }
}

public class PurchaseInvoiceService : TradeDocumentService<PurchaseInvoice>, IPurchaseInvoiceService
{
    public PurchaseInvoiceService(TradeInvoiceEngine engine) : base(engine) { }
}

public class SalesReturnService : TradeReturnService<SalesReturn, SalesInvoice>, ISalesReturnService
{
    public SalesReturnService(TradeInvoiceEngine engine) : base(engine) { }
}

public class PurchaseReturnService : TradeReturnService<PurchaseReturn, PurchaseInvoice>, IPurchaseReturnService
{
    public PurchaseReturnService(TradeInvoiceEngine engine) : base(engine) { }
}
