using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class ReturnLineDto
{
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
}
