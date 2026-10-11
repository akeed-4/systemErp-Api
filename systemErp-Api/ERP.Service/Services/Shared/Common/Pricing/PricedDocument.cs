using ERP.Core.Contracts.Shared;

namespace ERP.Service.Services.Shared;

/// <param name="Subtotal">Gross of all lines (quantity × price) before any discount.</param>
/// <param name="NetTotal">Taxable amount after line and invoice discounts (the frontend's invoice "subtotal").</param>
public sealed record PricedDocument(
    IReadOnlyList<PricedLine> Lines,
    decimal Subtotal,
    decimal ItemsDiscountTotal,
    decimal InvoiceDiscount,
    decimal DiscountTotal,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrandTotal);
