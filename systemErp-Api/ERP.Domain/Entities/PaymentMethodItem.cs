using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class PaymentMethodItem : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Type { get; set; } = "cash"; // cash, card, bank, cheque, credit
    public string LinkedAccountCode { get; set; } = string.Empty;
    public string LinkedAccountName { get; set; } = string.Empty;
    public string Icon { get; set; } = "payments";
    public decimal? CommissionPercent { get; set; }
    public bool RequiresReference { get; set; }
    public string Status { get; set; } = "Active";
}
