using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class VoucherPostingRequest
{
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public Guid VoucherId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public VoucherType Type { get; set; }
    public string PartyAccountCode { get; set; } = string.Empty;
    /// <summary>حسابات الخزينة المستلم/المصروف عبرها (مجموعها = المبلغ).</summary>
    public List<PaymentPosting> Treasury { get; set; } = new();
}
