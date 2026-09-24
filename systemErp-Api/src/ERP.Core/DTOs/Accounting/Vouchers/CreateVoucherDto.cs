namespace ERP.Core.DTOs.Accounting;

public partial class CreateVoucherDto
{
    public string VoucherNumber { get; set; } = string.Empty;
    public VoucherType Type { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string AmountInWordsAr { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public string PartyAccountCode { get; set; } = string.Empty;
    public string TreasuryAccountCode { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "cash";
    public bool IsSplitPayment { get; set; }
    public string? ReferenceNumber { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? JournalEntryId { get; set; }
    public string ReceivedOrPaidBy { get; set; } = string.Empty;
    public List<VoucherPaymentSplitDto> PaymentSplits { get; set; } = new();
}
