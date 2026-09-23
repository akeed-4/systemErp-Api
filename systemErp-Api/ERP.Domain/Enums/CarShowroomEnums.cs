namespace ERP.Domain.Enums;

/// <summary>
/// دورة مشتريات وتوريد السيارات - 7 مراحل معتمدة (تطابق واجهة Angular)
/// </summary>
public enum ProcurementStage
{
    Requisition = 1,            // 1. طلب بضاعة سيارات
    RequisitionApproved = 2,    // 2. تدقيق واعتماد الطلب
    Rfq = 3,                    // 3. استدراج عروض أسعار الموردين
    RfqApproved = 4,            // 4. فحص وترسية العرض المقبول
    PurchaseOrder = 5,          // 5. أمر الشراء الرسمي PO
    VinReceived = 6,            // 6. استلام الشواسي VIN
    Invoiced = 7                // 7. الفوترة النهائية وإقفال الدورة
}

public enum ProcurementOrderStatus
{
    Draft = 1,
    Approved = 2,
    InProgress = 3,
    Received = 4,
    Invoiced = 5,
    Closed = 6,
    Rejected = 7
}

public enum CarSalesCycleType
{
    Individual = 1,
    Corporate = 2,
    BankLease = 3,
    Installment = 4
}

public enum BuyerType
{
    Individual = 1,
    Corporate = 2,
    Government = 3
}

/// <summary>
/// دورة عقد بيع السيارة - 5 مراحل معتمدة (تطابق واجهة Angular)
/// </summary>
public enum SalesContractStatus
{
    Draft = 1,        // 1. طلبات ومسودات العقود
    Approved = 2,      // 2. تدقيق واعتماد العقود
    Allocated = 3,      // 3. تخصيص الشاسيه وتجهيز PDI
    Delivered = 4,      // 4. تسليم المركبة ومحضر الاستلام
    Invoiced = 5,        // 5. الفوترة والترحيل المالي
    Cancelled = 6
}

public enum VehicleCondition
{
    New = 1,
    Used = 2
}

public enum FuelType
{
    Petrol = 1,
    Diesel = 2,
    Hybrid = 3,
    Electric = 4
}

public enum TransmissionType
{
    Automatic = 1,
    Manual = 2,
    Cvt = 3
}

/// <summary>
/// أنماط احتساب ضريبة القيمة المضافة على مبيعات السيارات وفق أنظمة هيئة الزكاة والضريبة (ZATCA)
/// </summary>
public enum VatMode
{
    Standard15 = 1,      // ضريبة قياسية 15% على كامل القيمة
    ProfitMargin15 = 2,  // ضريبة هامش الربح 15% (سيارات مستعملة)
    MarginScheme = 3,    // نظام هامش الربح الكامل
    Exempt = 4           // معفى من الضريبة
}

public enum VehicleStatus
{
    Available = 1,
    Reserved = 2,
    Sold = 3
}
