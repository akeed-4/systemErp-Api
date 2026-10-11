using ERP.Core.Contracts.Shared;

namespace ERP.Service.Services.Shared;

public sealed record PricedLine(
    decimal Gross,
    decimal Discount,
    decimal AllocatedInvoiceDiscount,
    decimal Net,
    decimal VatRate,
    decimal VatAmount,
    decimal Total);
