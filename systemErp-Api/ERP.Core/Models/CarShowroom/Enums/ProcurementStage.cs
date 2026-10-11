namespace ERP.Core.Models.CarShowroom;

/// <summary>دورة مشتريات وتوريد السيارات - 7 مراحل معتمدة (تطابق واجهة Angular).</summary>
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
