using System.Globalization;

namespace ERP.Core.DTOs.Shared;

/// <summary>Localizes fixed API response messages using the request UI culture.</summary>
public static class ApiMessageLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["تم الإنشاء بنجاح"] = "Created successfully",
        ["تم التعديل بنجاح"] = "Updated successfully",
        ["تم الحذف بنجاح"] = "Deleted successfully",
        ["تم إنشاء بيان التسليم"] = "Delivery note created",
        ["تم تعديل بيان التسليم"] = "Delivery note updated",
        ["تم حذف بيان التسليم"] = "Delivery note deleted",
        ["تم حذف مرتجع التسليم"] = "Delivery return deleted",
        ["تم إنشاء مرتجع التسليم"] = "Delivery return created",
        ["تمت الفوترة"] = "Invoicing completed",
        ["تم إصدار الفاتورة"] = "Invoice issued",
        ["تم نقل العقد للمرحلة التالية"] = "Contract moved to the next stage",
        ["تمت فوترة المستخلص"] = "Milestone invoiced",
        ["تم إنشاء السند وترحيله"] = "Voucher created and posted",
        ["تم تعديل السند وإعادة ترحيله"] = "Voucher updated and reposted",
        ["تم حذف السند وعكس قيده"] = "Voucher deleted and its journal entry reversed",
        ["تم ترحيل القيد"] = "Journal entry posted",
        ["تم تعديل القيد"] = "Journal entry updated",
        ["تم حذف القيد"] = "Journal entry deleted",
        ["تم عكس القيد"] = "Journal entry reversed",
        ["تم تسجيل التسوية"] = "Adjustment recorded",
        ["تم تعديل التسوية"] = "Adjustment updated",
        ["تم حذف التسوية وإعادة احتساب الرصيد"] = "Adjustment deleted and balance recalculated",
        ["تم اعتماد الطلب"] = "Request approved",
        ["تم إنشاء فاتورة شراء مسودة"] = "Draft purchase invoice created",
        ["تم تحديث الوردية"] = "Shift updated",
        ["تم حذف الوردية"] = "Shift deleted",
        ["تم فتح الوردية"] = "Shift opened",
        ["تم إغلاق الوردية"] = "Shift closed",
        ["تم تسجيل الحركة النقدية"] = "Cash movement recorded",
        ["تم إلغاء الحركة النقدية"] = "Cash movement cancelled",
        ["تم حفظ سياسة التكلفة"] = "Costing policy saved",
        ["تم تحديث المرتجع"] = "Return updated",
        ["تم حذف المرتجع"] = "Return deleted",
        ["تم إنشاء المرتجع"] = "Return created",
        ["تم تحديث المعاملة"] = "Transaction updated",
        ["تم إلغاء المعاملة"] = "Transaction cancelled",
        ["تم حذف المعاملة"] = "Transaction deleted",
        ["تمت عملية البيع"] = "Sale completed",
        ["تم"] = "Done",
        ["تم إنشاء فاتورة مسودة"] = "Draft invoice created",
        ["تم إنشاء العقد"] = "Contract created",
        ["تم اعتماد العقد وإصدار الفاتورة"] = "Contract approved and invoice issued",
        ["تم التعديل"] = "Updated",
        ["تم إكمال العقد وإصدار الفاتورة"] = "Contract completed and invoice issued",
        ["تم إلغاء العقد"] = "Contract cancelled",
        ["تم الحذف"] = "Deleted",
        ["تم حفظ إعدادات الفاتورة"] = "Invoice settings saved",
        ["تم إنشاء الأمر"] = "Order created",
        ["تم إنشاء الفاتورة"] = "Invoice created",
        ["تم تعديل الفاتورة"] = "Invoice updated",
        ["تم ترحيل الفاتورة"] = "Invoice posted",
        ["تم حذف الفاتورة"] = "Invoice deleted",
        ["تم سحب الطلب"] = "Request withdrawn",
        ["تم إنشاء طلب الشراء"] = "Purchase order created",
        ["تم تسجيل فاتورة شراء المركبة"] = "Vehicle purchase invoice recorded",
        ["تم نقل الأمر للمرحلة التالية"] = "Order moved to the next stage",
        ["تم رفض الأمر"] = "Order rejected",
        ["تم إرسال الرد"] = "Reply sent",
        ["تم تحديث حالة التذكرة"] = "Ticket status updated",
        ["تم إنشاء رابط الدفع"] = "Payment link created",
        ["تم إلغاء عملية الدفع"] = "Payment cancelled",
        ["تم حفظ إعدادات بوابة الدفع"] = "Payment gateway settings saved",
        ["تم تحديث بيانات الملف الشخصي بنجاح"] = "Profile updated successfully",
        ["تم تحديث بيانات المنشأة"] = "Company details updated",
        ["تم حفظ إعدادات الربط"] = "Integration settings saved",
        ["تم حفظ تحديثات الصلاحيات بنجاح"] = "Permissions updated successfully",
        ["تم تغيير الباقة"] = "Plan changed",
        ["تم تمديد الاشتراك"] = "Subscription extended",
        ["تم تحديث حالة الاشتراك"] = "Subscription status updated",
        ["تم تحديث الوحدات"] = "Modules updated",
        ["تم حفظ الباقة"] = "Plan saved",
        ["تم تفعيل الباقة"] = "Plan activated",
        ["تم إنشاء المستخدم"] = "User created",
        ["تم تعديل المستخدم"] = "User updated",
        ["تم تعطيل المستخدم"] = "User deactivated",
    };

    public static string? Localize(string? message)
    {
        if (message is null || !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
            return message;

        return English.TryGetValue(message, out var translated) ? translated : message;
    }
}
