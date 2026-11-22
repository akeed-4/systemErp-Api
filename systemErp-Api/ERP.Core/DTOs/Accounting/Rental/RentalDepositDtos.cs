namespace ERP.Core.DTOs.Accounting;

public class RentalDepositDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public decimal Amount { get; set; }
    public decimal AppliedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal Balance { get; set; }
    public DateTime ReceivedAt { get; set; }
    public Guid? ReceiptVoucherId { get; set; }
    public string? ReceiptVoucherNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<RentalDepositSettlementDto> Settlements { get; set; } = new();
}

public class RentalDepositSettlementDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime SettledAt { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public Guid? VoucherId { get; set; }
    public string? VoucherNumber { get; set; }
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>قبض تأمين: طريقة الدفع (أو حساب الخزينة) والمبلغ؛ التاريخ اختياري (اليوم).</summary>
public class ReceiveRentalDepositDto
{
    public Guid ContractId { get; set; }
    public decimal Amount { get; set; }
    public DateTime? Date { get; set; }
    public string? PaymentMethodCode { get; set; }
    public string? TreasuryAccountCode { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

/// <summary>خصم من فاتورة فوترة للعقد نفسه؛ المبلغ الفارغ = الأقل من الرصيد ومتبقي الفاتورة.</summary>
public class ApplyRentalDepositDto
{
    public Guid InvoiceId { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? Date { get; set; }
}

/// <summary>رد للعميل؛ المبلغ الفارغ = كامل الرصيد.</summary>
public class RefundRentalDepositDto
{
    public decimal? Amount { get; set; }
    public DateTime? Date { get; set; }
    public string? PaymentMethodCode { get; set; }
    public string? TreasuryAccountCode { get; set; }
    public string? Reason { get; set; }
}
