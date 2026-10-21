using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// فواتير معارض السيارات بكل أنواعها (جدول Invoice): المنطق في TradeInvoiceEngine، وأسطر السيارات في CarInvoiceExtension.
/// فواتير التجارة العامة لا تُقبل هنا: لها خدماتها وجداولها (ISalesInvoiceService وأخواتها).
/// </summary>
public class InvoiceService : IInvoiceService
{
    private readonly TradeInvoiceEngine _engine;

    public InvoiceService(TradeInvoiceEngine engine) => _engine = engine;

    public Task<PagedResult<InvoiceDto>> ListAsync(InvoiceKind? kind, PaginationParams p, bool? hasVehicleLines = null, CancellationToken ct = default)
        => _engine.ListAsync(kind, p, VehicleFilter(hasVehicleLines), ct);

    public Task<LoadResult> LoadAsync(InvoiceKind? kind, DataSourceLoadOptions options, bool? hasVehicleLines = null, CancellationToken ct = default)
        => _engine.LoadAsync(kind, options, VehicleFilter(hasVehicleLines), ct);

    private static System.Linq.Expressions.Expression<Func<Invoice, bool>>? VehicleFilter(bool? hasVehicleLines) => hasVehicleLines switch
    {
        true => i => i.VehicleLines.Any(),
        false => i => !i.VehicleLines.Any(),
        null => null,
    };

    public Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default) => _engine.GetAsync<Invoice>(id, ct);
    public Task<InvoiceDto> CreateAsync(CreateInvoiceDto request, CancellationToken ct = default) => _engine.CreateAsync<Invoice>(request, ct);
    public Task<InvoiceDto> UpdateAsync(Guid id, UpdateInvoiceDto request, CancellationToken ct = default) => _engine.UpdateAsync<Invoice>(id, request, ct);
    public Task<InvoiceDto> CreateReturnAsync(CreateReturnInvoiceRequestDto request, CancellationToken ct = default) => _engine.CreateReturnAsync<Invoice, Invoice>(request, ct);
    public Task<InvoiceDto> PostDraftAsync(Guid id, CancellationToken ct = default) => _engine.PostDraftAsync<Invoice>(id, ct);
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => _engine.DeleteAsync<Invoice>(id, ct);
    public Task<ZatcaSubmitResultDto> SubmitToZatcaAsync(Guid id, CancellationToken ct = default) => _engine.SubmitToZatcaAsync<Invoice>(id, ct);
    public Task<InvoiceDto> SubmitToZatcaIfRequestedAsync(Guid id, CancellationToken ct = default) => _engine.SubmitIfRequestedAsync<Invoice>(id, ct);
    public Task<InvoiceJournalDto> PreviewJournalAsync(CreateInvoiceDto request, CancellationToken ct = default) => _engine.PreviewJournalAsync<Invoice>(request, ct);
    public Task<InvoiceJournalDto> GetJournalAsync(Guid id, CancellationToken ct = default) => _engine.GetJournalAsync<Invoice>(id, ct);
}
