using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class VatReturnDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal StandardRatedSales { get; set; }
    public decimal OutputVat { get; set; }
    public decimal StandardRatedPurchases { get; set; }
    public decimal InputVat { get; set; }
    public decimal NetVatPayable { get; set; }
}
