namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// تحديث جزئي لإعدادات الحسابات: الحقل غير المرسَل (null) يبقى كما هو، والنص الفارغ "" يمسح الحساب فيعود لحسابه الافتراضي.
/// </summary>
public class SaveAccountingSettingsDto
{
    public InventorySystem? InventoryMethod { get; set; }
    /// <summary>بداية السنة المالية التي تسري منها الطريقة الجديدة؛ مطلوب لتغيير الطريقة بعد أول قيد.</summary>
    public DateTime? InventoryMethodEffectiveFrom { get; set; }

    public string? InventoryAccountCode { get; set; }
    public string? CogsAccountCode { get; set; }
    public string? InventoryShortageAccountCode { get; set; }
    public string? InventorySurplusAccountCode { get; set; }
    public string? PurchasesAccountCode { get; set; }
    public string? PurchaseReturnsAccountCode { get; set; }
    public string? OpeningInventoryAccountCode { get; set; }
    public string? EndingInventoryAccountCode { get; set; }
    public string? SalesRevenueAccountCode { get; set; }
    public string? SalesReturnsAccountCode { get; set; }
    public string? OutputVatAccountCode { get; set; }
    public string? InputVatAccountCode { get; set; }
    public string? OutputExciseAccountCode { get; set; }
    public string? ContractAdvancesReceivedAccountCode { get; set; }
    public string? ContractAdvancesPaidAccountCode { get; set; }
    public string? ContractRetentionReceivableAccountCode { get; set; }
    public string? ContractRetentionPayableAccountCode { get; set; }
    public string? ContractPenaltiesExpenseAccountCode { get; set; }
    public string? ContractPenaltiesIncomeAccountCode { get; set; }
    public string? ContractOtherDeductionsAccountCode { get; set; }
    public string? ProjectMaterialsWipAccountCode { get; set; }
    public string? ContractCostOfRevenueAccountCode { get; set; }
    public string? ProjectLaborCostAccountCode { get; set; }
    public string? ProjectEquipmentCostAccountCode { get; set; }
    public string? ProjectAbsorbedCostAccountCode { get; set; }
    /// <summary>فترة إرجاع المبيعات بالأيام: null = بلا تغيير، 0 = بلا حد.</summary>
    public int? SalesReturnPeriodDays { get; set; }
    /// <summary>فترة إرجاع المشتريات بالأيام: null = بلا تغيير، 0 = بلا حد.</summary>
    public int? PurchaseReturnPeriodDays { get; set; }
}

public class AccountingSettingsDto : SaveAccountingSettingsDto
{
    public InventorySystem? PreviousInventoryMethod { get; set; }
    /// <summary>الحسابات الفعّالة بعد تطبيق الافتراضي على الفارغ (مفتاحها اسم الإعداد).</summary>
    public Dictionary<string, string?> EffectiveAccounts { get; set; } = new();
}
