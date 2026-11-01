using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class AccountLedgerEntryDto
{
    /// <summary>معرّف سطر القيد.</summary>
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
    public string? ReferenceNumber { get; set; }
    /// <summary>نوع المستند المصدر للقيد (sales، receipt_voucher، manual...)؛ الواجهة تترجمه إلى اسم السند.</summary>
    public string? ReferenceType { get; set; }
    /// <summary>رقم المستند المصدر، أو رقم القيد إن لم يكن له مستند.</summary>
    public string DocumentNumber { get; set; } = string.Empty;
    /// <summary>الرصيد الجاري بعملة الحساب (الرصيد ÷ سعر صرف عملة الحساب الحالي).</summary>
    public decimal RunningBalanceInAccountCurrency { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostCenterCode { get; set; }
}
