namespace RayahAccounting.Application.Interfaces;

public record VatDeclarationReportDto(
    string Period,
    DateTime FromDate,
    DateTime ToDate,
    decimal StandardRatedSales,
    decimal StandardRatedSalesVat,
    decimal ZeroRatedSales,
    decimal ExemptSales,
    decimal TotalSalesWithVat,
    decimal StandardRatedPurchases,
    decimal StandardRatedPurchasesVat,
    decimal ZeroRatedPurchases,
    decimal ExemptPurchases,
    decimal TotalPurchasesWithVat,
    decimal TotalOutputVat,
    decimal TotalInputVat,
    decimal NetVatDueOrRefund
);

public interface IReportsService
{
    Task<VatDeclarationReportDto> GetVatReturnReportAsync(int year, int quarter, CancellationToken ct = default);
}
