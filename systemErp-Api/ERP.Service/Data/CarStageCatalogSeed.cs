using ERP.Core.Models.CarShowroom;

namespace ERP.Service.Data;

public static class CarStageCatalogSeed
{
    private const string P = CarStageDefinition.Procurement;
    private const string S = CarStageDefinition.SalesContract;

    public static readonly CarStageDefinition[] All =
    [
        new() { WorkflowType = P, Id = "requisition", Number = 1, NameAr = "طلب بضاعة سيارات", NameEn = "Requisition", DescAr = "تسجيل وتحديد الاحتياج من ماركات وموديلات السيارات لتغذية صالة العرض والمخازن.", DescEn = "Recording the required car brands and models to stock the showroom and warehouses.", Icon = "post_add", Color = "indigo" },
        new() { WorkflowType = P, Id = "requisition_approved", Number = 2, NameAr = "تدقيق واعتماد طلب البضاعة", NameEn = "Requisition Approval", DescAr = "المراجعة الإدارية والمالية وتدقيق الميزانية المخصصة وتوقيع اعتماد طلب الشراء.", DescEn = "Administrative and financial review, budget check, and sign-off of the purchase request.", Icon = "verified", Color = "blue" },
        new() { WorkflowType = P, Id = "rfq", Number = 3, NameAr = "عروض أسعار الموردين (RFQ)", NameEn = "Supplier Quotations (RFQ)", DescAr = "إرسال واستقبال طلبات التسعير من الوكلاء المعتمدين والموزعين للمفاضلة التنافسية.", DescEn = "Sending and receiving price requests from authorized agents and distributors for comparison.", Icon = "request_quote", Color = "purple" },
        new() { WorkflowType = P, Id = "rfq_approved", Number = 4, NameAr = "فحص واعتماد عرض السعر المقبول", NameEn = "Quotation Approval", DescAr = "اختيار العرض الأنسب سعراً ومواصفات وتوقيع قرار الاعتماد.", DescEn = "Selecting the best offer on price and specifications and signing the approval.", Icon = "published_with_changes", Color = "amber" },
        new() { WorkflowType = P, Id = "purchase_order", Number = 5, NameAr = "أمر الشراء الرسمي (PO)", NameEn = "Purchase Order (PO)", DescAr = "إصدار أمر الشراء الرسمي الملزم قانونياً للوكيل مع تحديد شروط الدفع والتسليم.", DescEn = "Issuing the legally binding purchase order to the agent with payment and delivery terms.", Icon = "shopping_cart_checkout", Color = "cyan" },
        new() { WorkflowType = P, Id = "vin_received", Number = 6, NameAr = "استلام الشواسي VIN وإدخال المخزن", NameEn = "VIN Receiving & Inspection", DescAr = "استلام السيارات فعلياً ومطابقة أرقام الشواسي والمحركات والبطاقات الجمركية وإدخال المخزن.", DescEn = "Physically receiving the cars, matching VINs, engine numbers, and customs cards, and stocking them.", Icon = "local_shipping", Color = "emerald" },
        new() { WorkflowType = P, Id = "invoiced", Number = 7, NameAr = "الفوترة النهائية وإقفال الدورة", NameEn = "Invoicing & Closing", DescAr = "استلام فاتورة المورد الضريبية ومطابقتها وترحيل القيد المحاسبي ومرتجع الشراء.", DescEn = "Receiving and matching the supplier tax invoice, posting the journal entry, and purchase returns.", Icon = "receipt_long", Color = "teal" },

        new() { WorkflowType = S, Id = "drafts", Number = 1, NameAr = "طلبات ومسودات العقود", NameEn = "Contract Drafts", DescAr = "إنشاء طلب بيع سيارة جديد، تسجيل بيانات المشتري، واختيار نوع دورة البيع (نقدي / تأجيري / شركات).", DescEn = "Creating a new car sale request, recording buyer details, and choosing the sales cycle (cash / lease / corporate).", Icon = "post_add", Color = "slate" },
        new() { WorkflowType = S, Id = "approvals", Number = 2, NameAr = "تدقيق واعتماد العقود", NameEn = "Contract Approvals", DescAr = "المراجعة الائتمانية وتدقيق المستندات والموافقة البنكية أو سداد الدفعة الأولى واعتماد العقد رسمياً.", DescEn = "Credit review, document checks, bank approval or down payment, and formal contract approval.", Icon = "verified", Color = "indigo" },
        new() { WorkflowType = S, Id = "allocation", Number = 3, NameAr = "تخصيص الشواسي وتجهيز PDI", NameEn = "VIN Allocation & PDI", DescAr = "ربط العقد المعتمد برقم شاسي VIN محدد من المستودع، وبدء فحص الجودة والتجهيز الفني PDI.", DescEn = "Linking the approved contract to a specific VIN from the warehouse and starting the PDI inspection.", Icon = "pin", Color = "blue" },
        new() { WorkflowType = S, Id = "delivery", Number = 4, NameAr = "تسليم المركبة ومحضر الاستلام", NameEn = "Delivery & Handover", DescAr = "تسليم السيارة للمشتري رسمياً وتوقيع محضر الاستلام والفحص النهائي واستلام المفاتيح.", DescEn = "Formally handing the car to the buyer, signing the handover record, final inspection, and key handover.", Icon = "how_to_reg", Color = "emerald" },
        new() { WorkflowType = S, Id = "invoicing", Number = 5, NameAr = "الفوترة والترحيل المالي", NameEn = "Invoicing & Financial Posting", DescAr = "إصدار الفاتورة الضريبية النهائية وتطبيق ضريبة القيمة المضافة (15% أو هامش الربح) وترحيل القيد.", DescEn = "Issuing the final tax invoice, applying VAT (15% or margin scheme), and posting the journal entry.", Icon = "receipt_long", Color = "purple" },
    ];
}
