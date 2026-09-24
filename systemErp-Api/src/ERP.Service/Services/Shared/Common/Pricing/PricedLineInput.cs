using ERP.Core.Contracts.Shared;

namespace ERP.Service.Services.Shared;

/// <param name="Discount">Line discount as an amount (SAR), as the frontend sends it.</param>
/// <param name="VatRate">Percentage, e.g. 15 (never 0.15).</param>
public sealed record PricedLineInput(decimal Quantity, decimal UnitPrice, decimal Discount, decimal VatRate, decimal? VatOverride = null);
