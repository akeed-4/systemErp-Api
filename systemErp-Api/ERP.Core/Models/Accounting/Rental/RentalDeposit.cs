namespace ERP.Core.Models.Accounting;

/// <summary>
/// تأمين مقبوض على عقد تأجير (R3b): سند قبض قائم (مدين الخزينة، دائن تأمينات العملاء) يُنشئه النظام. رصيده = المبلغ − ما سُوّي منه
/// (خصم من فاتورة العقد أو رد للعميل). لا يُحذف إلا بلا تسويات، فيُحذف سنده ويُعكس قيده.
/// </summary>
public class RentalDeposit : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime ReceivedAt { get; set; }
    public Guid? ReceiptVoucherId { get; set; }
    public string? ReceiptVoucherNumber { get; set; }
    public string Status { get; set; } = RentalDepositStatuses.Held;
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
    public virtual ICollection<RentalDepositSettlement> Settlements { get; set; } = new List<RentalDepositSettlement>();
}

/// <summary>
/// تسوية من تأمين: خصم من فاتورة العقد (سند قبض موزّع على الفاتورة: مدين التأمينات، دائن العميل) أو رد (سند صرف: مدين التأمينات،
/// دائن الخزينة). الإلغاء يحذف سنده ويعكس قيده فيعود الرصيد.
/// </summary>
public class RentalDepositSettlement : BaseEntity
{
    public Guid DepositId { get; set; }
    public string Kind { get; set; } = RentalDepositSettlementKinds.Apply;
    public decimal Amount { get; set; }
    public DateTime SettledAt { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public Guid? VoucherId { get; set; }
    public string? VoucherNumber { get; set; }
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
    public virtual RentalDeposit Deposit { get; set; } = null!;
}

public static class RentalDepositStatuses
{
    /// <summary>عليه رصيد.</summary>
    public const string Held = "held";
    /// <summary>سُوّي كله (خصماً أو رداً).</summary>
    public const string Settled = "settled";
    public const string Cancelled = "cancelled";
}

public static class RentalDepositSettlementKinds
{
    public const string Apply = "apply";
    public const string Refund = "refund";
}
