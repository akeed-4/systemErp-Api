namespace ERP.Domain.Enums;

public enum InvoiceType
{
    StandardTaxInvoice = 1,      // فاتورة ضريبية (B2B)
    SimplifiedTaxInvoice = 2,    // فاتورة ضريبية مبسطة (B2C)
    DebitNote = 3,               // إشعار مدين
    CreditNote = 4,              // إشعار دائن
    PurchaseInvoice = 5          // فاتورة مشتريات
}

public enum VoucherType
{
    Receipt = 1,                 // سند قبض
    Payment = 2                  // سند صرف
}

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Mada = 3,
    CreditCard = 4,
    Cheque = 5,
    ApplePay = 6
}

public enum AccountCategory
{
    Asset = 1,                  // أصول
    Liability = 2,             // خصوم / التزامات
    Equity = 3,                  // حقوق ملكية
    Revenue = 4,                // إيرادات
    Expense = 5                 // مصروفات
}

public enum LinkedEntityType
{
    General = 0,
    Customer = 1,
    Supplier = 2,
    Bank = 3
}

public enum ZatcaEnvironment
{
    Sandbox = 1,
    Simulation = 2,
    Production = 3
}

public enum ComplianceStatus
{
    NotEnrolled = 0,
    InProgress = 1,
    Compliant = 2
}

public enum ZatcaSubmissionStatus
{
    NotSubmitted = 0,
    Cleared = 1,
    Reported = 2,
    Rejected = 3,
    Warning = 4
}

public enum CostingMethod
{
    MovingAverage = 1,
    FIFO = 2,
    LastPurchase = 3,
    Standard = 4
}

public enum StockMovementType
{
    InPurchase = 1,              // وارد مشتريات
    OutSales = 2,                 // منصرف مبيعات
    AdjustmentIn = 3,            // تسوية جردية - إضافة
    AdjustmentOut = 4             // تسوية جردية - حذف
}

public enum JournalEntryStatus
{
    Draft = 1,                   // مسودة
    Posted = 2,                  // معتمد ومرحل
    Reversed = 3                 // معكوس / ملغي
}

public enum SubscriptionPlanId
{
    Starter = 1,
    Professional = 2,
    Enterprise = 3
}

public enum SubscriptionBillingCycle
{
    Monthly = 1,
    Yearly = 2
}

public enum SubscriptionStatus
{
    Active = 1,
    Trial = 2,
    Expired = 3,
    Suspended = 4
}

public enum UserRole
{
    Owner = 1,
    Admin = 2,
    GeneralManager = 3,
    ChiefAccountant = 4,
    SalesOrder = 5
}
