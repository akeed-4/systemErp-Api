using Microsoft.EntityFrameworkCore;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;

namespace ERP.Application.Reports;

public class ReportsService : IReportsService
{
    private readonly IApplicationDbContext _db;

    public ReportsService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<VatDeclarationReportDto> GetVatReturnReportAsync(int year, int quarter, CancellationToken ct = default)
    {
        var startMonth = ((quarter - 1) * 3) + 1;
        var fromDate = new DateTime(year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = fromDate.AddMonths(3).AddTicks(-1);

        var invoices = await _db.Invoices
            .Where(i => i.IssueDate >= fromDate && i.IssueDate <= toDate)
            .ToListAsync(ct);

        var salesInvoices = invoices.Where(i => i.InvoiceType != InvoiceType.PurchaseInvoice).ToList();
        var purchaseInvoices = invoices.Where(i => i.InvoiceType == InvoiceType.PurchaseInvoice).ToList();

        var standardSales = salesInvoices.Sum(i => i.Subtotal);
        var standardSalesVat = salesInvoices.Sum(i => i.VatTotal);

        var standardPurchases = purchaseInvoices.Sum(i => i.Subtotal);
        var standardPurchasesVat = purchaseInvoices.Sum(i => i.VatTotal);

        var netVat = standardSalesVat - standardPurchasesVat;

        return new VatDeclarationReportDto(
            $"الربع {quarter} لعام {year}",
            fromDate,
            toDate,
            standardSales,
            standardSalesVat,
            0m,
            0m,
            standardSales + standardSalesVat,
            standardPurchases,
            standardPurchasesVat,
            0m,
            0m,
            standardPurchases + standardPurchasesVat,
            standardSalesVat,
            standardPurchasesVat,
            netVat
        );
    }
}
