using ERP.Core.Contracts.Shared;

namespace ERP.Service.Services.Shared;

public sealed record PricedLine(
    decimal Gross,
    decimal Discount,
    decimal AllocatedInvoiceDiscount,
    decimal Net,
    decimal ExciseTaxRate,
    decimal ExciseRetailPrice,
    decimal ExciseStandardPrice,
    decimal ExciseTaxBaseAmount,
    decimal ExciseTaxAmount,
    decimal VatRate,
    decimal VatAmount,
    decimal Total);
