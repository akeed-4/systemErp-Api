namespace ERP.Core.Models.Shared;

/// <summary>نوع المستند (InvoiceKind في الواجهة: sales|purchase|sales_return|purchase_return).</summary>
public enum InvoiceKind
{
    Sales = 1,
    Purchase = 2,
    SalesReturn = 3,
    PurchaseReturn = 4
}
