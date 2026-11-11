using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting.Sales;

internal static class RentalInvoiceClassification
{
    public static decimal ExpenseForResource(InvoiceBase document, Guid resourceId, Guid? costCenterId = null)
    {
        if (!string.IsNullOrWhiteSpace(document.InventoryAccountCode)) return 0;
        var nonStock = document.Lines.Where(InvoiceLineStock.IsNonStock).ToList();
        if (costCenterId.HasValue)
            nonStock = nonStock.Where(line => line.CostCenterId == costCenterId.Value).ToList();

        var total = nonStock.Sum(line => line.TotalBeforeVat);
        if (total <= 0) return 0;

        var fx = document.ExchangeRate <= 0 ? 1 : document.ExchangeRate;
        var baseNet = DocumentPricing.Round(document.GrandTotal * fx) - DocumentPricing.Round(document.VatTotal * fx);
        var centerBaseNet = costCenterId.HasValue
            ? DocumentPricing.Round(nonStock.Sum(line => line.TotalBeforeVat) * fx)
            : baseNet;
        var expense = Math.Min(Math.Max(0, centerBaseNet), DocumentPricing.Round(total * fx));
        var classified = nonStock.Where(line => line.ProjectResourceId == resourceId).Sum(line => line.TotalBeforeVat);
        return DocumentPricing.Round(expense * classified / total);
    }

    public static decimal ResourceExpenseRatioAtCenter(InvoiceBase document, IReadOnlyCollection<Guid> resourceIds, Guid costCenterId)
    {
        if (!string.IsNullOrWhiteSpace(document.InventoryAccountCode)) return 0;
        if (resourceIds.Count == 0) return 0;

        var nonStock = document.Lines.Where(line => InvoiceLineStock.IsNonStock(line) && line.CostCenterId == costCenterId).ToList();
        var total = nonStock.Sum(line => line.TotalBeforeVat);
        if (total <= 0) return 0;

        var classified = nonStock.Where(line => line.ProjectResourceId.HasValue && resourceIds.Contains(line.ProjectResourceId.Value))
            .Sum(line => line.TotalBeforeVat);
        return Math.Clamp(classified / total, 0, 1);
    }
}
