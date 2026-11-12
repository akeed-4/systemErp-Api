using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تغطية مادة المشروع من سطر متابعة التوريد (بوحدة الصنف). كل كمية في مرحلة واحدة فلا تُحسب مرتين:
/// <list type="bullet">
/// <item>المتبقي للتسليم = المطلوب − المسلَّم الصافي (ما خرج للموقع لا يُنتظر من الرصيد ولا من الشراء).</item>
/// <item>رصيد المستودع يشمل المستلم بفواتير مرحّلة ولم يُسلَّم بعد: لا يُطرح المستلم مرة ثانية.</item>
/// <item>في الطريق = المعلّق في طلبات غير محوّلة + المطلوب في فواتير شراء مسودة؛ الطلب المحوَّل ليس معلّقاً.</item>
/// </list>
/// صافي الشراء = max(0, المتبقي للتسليم − الرصيد − في الطريق)، والمقترح لطلب جديد لا يتجاوز حد السرفر (المتبقي للطلب).
/// الرصيد غير محجوز للمشروع (لا حجز مخزون في النظام): قد يصرفه مشروع آخر.
/// </summary>
internal static class MaterialCoverage
{
    private const int Decimals = 4;
    public const string Info = "info", Warning = "warning", Critical = "critical";

    // رموز التنبيه (الواجهة تترجم السبب والإجراء المقترح لكل رمز)
    public const string FullyAvailable = "fully_available";
    public const string FullyDelivered = "fully_delivered";
    public const string PartiallyAvailable = "partially_available";
    public const string NoStock = "no_stock";
    public const string PendingRequests = "pending_requests";
    public const string OrderedNotReceived = "ordered_not_received";
    public const string PurchaseNeeded = "purchase_needed";
    public const string RequestLimit = "request_limit";
    public const string ExcessPurchase = "excess_purchase";
    public const string OutsidePlan = "outside_plan";
    public const string PlanNotApproved = "plan_not_approved";
    public const string NotStocked = "not_stocked";
    public const string UnitMismatch = "unit_mismatch";

    public static void Apply(ProjectMaterialTrackingLineDto l, bool planApproved)
    {
        var need = Math.Max(0, l.RemainingToDeliver);
        var stock = Math.Max(0, l.WarehouseStock);
        var inTransit = l.PendingRequestQuantity + l.OrderedQuantity;
        var afterStock = Math.Max(0, need - stock);
        l.NetToPurchase = Math.Round(Math.Max(0, afterStock - inTransit), Decimals);
        l.SuggestedRequestQuantity = Math.Round(Math.Min(l.NetToPurchase, Math.Max(0, l.RemainingToRequest)), Decimals);
        l.ExcessPurchaseQuantity = Math.Round(Math.Max(0, inTransit - afterStock), Decimals);
        l.Alerts.Clear();

        if (!planApproved)
        {
            // بلا احتياجات معتمدة لا مطلوب يُقاس عليه: لا اكتمال ولا نقص
            l.CoverageStatus = MaterialCoverageStatuses.Unverifiable;
            l.NetToPurchase = 0; l.SuggestedRequestQuantity = 0; l.ExcessPurchaseQuantity = 0;
            Add(l, PlanNotApproved, Warning, l.RequestedQuantity);
            return;
        }

        l.CoverageStatus = need <= 0 || stock >= need ? MaterialCoverageStatuses.Full
            : stock > 0 ? MaterialCoverageStatuses.Partial : MaterialCoverageStatuses.None;

        if (l.OutsidePlan) Add(l, OutsidePlan, Warning, l.RequestedQuantity + l.DeliveredQuantity);
        else if (need <= 0) Add(l, FullyDelivered, Info, l.DeliveredQuantity);
        else if (stock >= need) Add(l, FullyAvailable, Info, need);
        else if (stock > 0) Add(l, PartiallyAvailable, l.NetToPurchase > 0 ? Warning : Info, afterStock);
        else Add(l, NoStock, l.NetToPurchase > 0 ? Critical : Warning, need);

        if (l.PendingRequestQuantity > 0) Add(l, PendingRequests, Info, l.PendingRequestQuantity);
        if (l.OrderedQuantity > 0) Add(l, OrderedNotReceived, Info, l.OrderedQuantity);
        if (l.NetToPurchase > 0) Add(l, PurchaseNeeded, Warning, l.NetToPurchase);
        // صافي الشراء أكثر من حد الطلب (الرصيد صُرف لغير المشروع، أو طلبات محوّلة لم تُستلم كاملة): يلزم تعديل الاحتياجات
        if (l.NetToPurchase > l.SuggestedRequestQuantity) Add(l, RequestLimit, Warning, l.NetToPurchase - l.SuggestedRequestQuantity);
        if (l.ExcessPurchaseQuantity > 0) Add(l, ExcessPurchase, Warning, l.ExcessPurchaseQuantity);
    }

    private static void Add(ProjectMaterialTrackingLineDto l, string code, string severity, decimal qty)
        => l.Alerts.Add(new ProjectSupplyAlertDto { Code = code, Severity = severity, Quantity = Math.Round(qty, Decimals) });
}
