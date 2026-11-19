using System.Globalization;

namespace ERP.Service.Services.Shared;

/// <summary>
/// مستند يُعتمد من شاشته ويُتابَع اعتماده: كيانه، وحالته كما تُقرأ من أعمدته، وتصنيف كل حالة (بانتظار الاعتماد/معتمد/مرفوض/مُعاد؛
/// ما سواها بعد الانتظار = إلغاء)، وشاشة صلاحية «اعتماد»، ورابط فتحه في الواجهة.
/// </summary>
public sealed class ApprovalTrackedDocument
{
    public required Type EntityType { get; init; }
    public required string Key { get; init; }
    public required string ScreenId { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    /// <summary>أعمدة الحالة المراقَبة (تغيّر أيٍّ منها يعيد التصنيف).</summary>
    public required string[] StatusProperties { get; init; }
    /// <summary>حالة المستند من قارئ أعمدته (القيمة الحالية أو الأصلية)، نصاً يُصنَّف بالمجموعات أدناه.</summary>
    public required Func<Func<string, string?>, string?> State { get; init; }
    public required string[] Pending { get; init; }
    public string[] Approved { get; init; } = Array.Empty<string>();
    public string[] Rejected { get; init; } = Array.Empty<string>();
    /// <summary>
    /// حالات «أُعيد للتعديل». تُصنَّف هكذا فقط عند الخروج من الانتظار (كثير من المستندات يُعاد إلى «مسودة» نفسها).
    /// </summary>
    public string[] Returned { get; init; } = Array.Empty<string>();
    public required Func<object, string?> Number { get; init; }
    public Func<object, decimal?>? Amount { get; init; }
    /// <summary>مقدّم المستند إن عُرف من الكيان (وإلا CreatedByUserId إن وُجد، وإلا من حفظه).</summary>
    public Func<object, Guid?>? Requester { get; init; }
    /// <summary>رابط فتح المستند في الواجهة (مسار + query).</summary>
    public required Func<object, string> Url { get; init; }
    /// <summary>false إن كانت خدمة المستند تُبلغ مقدّمه بالنتيجة بنفسها (منع التكرار).</summary>
    public bool NotifyRequester { get; init; } = true;

    public string Name => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? NameEn : NameAr;

    /// <summary>تصنيف قيمة الحالة: pending / approved / rejected / returned، أو null لغيرها.</summary>
    public string? Classify(string? state)
    {
        if (state == null) return null;
        static bool In(string[] set, string s) => set.Any(v => string.Equals(v, s, StringComparison.OrdinalIgnoreCase));
        if (In(Pending, state)) return DocumentApprovalTrackStatuses.Pending;
        if (In(Approved, state)) return DocumentApprovalTrackStatuses.Approved;
        if (In(Rejected, state)) return DocumentApprovalTrackStatuses.Rejected;
        if (In(Returned, state)) return DocumentApprovalTrackStatuses.Returned;
        return null;
    }
}

/// <summary>
/// سجل المستندات المتابَعة. إضافة مستند جديد = سطر هنا (بلا تعديل خدمته). المستندات التي لا حالة انتظار لها (المصروفات المقدمة،
/// السلف) تُعتمد عبر سياسات الموافقة ومركز الاعتماد فقط، فليست هنا.
/// </summary>
public static class ApprovalTrackedDocuments
{
    /// <summary>مستند بعمود حالة واحد (نص أو enum: يُقارن باسمه).</summary>
    private static ApprovalTrackedDocument Doc<T>(string key, string screenId, string nameAr, string nameEn, string statusProperty,
        string[] pending, string[] approved, Func<T, string?> number, Func<T, string> url,
        string[]? rejected = null, string[]? returned = null,
        Func<T, decimal?>? amount = null, Func<T, Guid?>? requester = null, bool notifyRequester = true) where T : class
        => new()
        {
            EntityType = typeof(T), Key = key, ScreenId = screenId, NameAr = nameAr, NameEn = nameEn,
            StatusProperties = new[] { statusProperty }, State = get => get(statusProperty),
            Pending = pending, Approved = approved, Rejected = rejected ?? Array.Empty<string>(), Returned = returned ?? Array.Empty<string>(),
            Number = o => number((T)o), Url = o => url((T)o), Amount = amount == null ? null : o => amount((T)o),
            Requester = requester == null ? null : o => requester((T)o), NotifyRequester = notifyRequester,
        };

    private static string[] S(params string[] values) => values;
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Project(Guid contractId, string section) => $"/construction/projects/{contractId}/{section}";

    public static readonly IReadOnlyList<ApprovalTrackedDocument> All = new List<ApprovalTrackedDocument>
    {
        // ---------- معرض السيارات ----------
        Doc<LandedCostVoucher>("landed_cost_voucher", PermissionService.LandedCostApprovalScreenId, "سند تكاليف مستوردة", "Landed cost voucher",
            nameof(LandedCostVoucher.Status), S(nameof(LandedCostVoucherStatus.Draft)), S(nameof(LandedCostVoucherStatus.Approved)),
            v => v.VoucherNumber, _ => "/showroom/landed-costs", amount: v => v.GrandTotal),
        Doc<ImportCostAdvance>("import_cost_advance", PermissionService.LandedCostApprovalScreenId, "دفعة مقدمة لتكاليف الاستيراد", "Import cost advance",
            nameof(ImportCostAdvance.Status), S(nameof(ImportCostAdvanceStatus.Draft)),
            S(nameof(ImportCostAdvanceStatus.Open), nameof(ImportCostAdvanceStatus.PartiallySettled), nameof(ImportCostAdvanceStatus.Settled)),
            a => a.Number, _ => "/showroom/import-cost-advances", amount: a => a.Amount),
        // أمر الشراء: الانتظار مرحلتا «طلب» و«عرض سعر» قبل اعتمادهما (عمودا المرحلة والحالة معاً)
        new()
        {
            EntityType = typeof(CarProcurementOrder), Key = "car_procurement_order", ScreenId = "car-showroom",
            NameAr = "أمر شراء مركبات", NameEn = "Vehicle procurement order",
            StatusProperties = new[] { nameof(CarProcurementOrder.Stage), nameof(CarProcurementOrder.Status) },
            State = get =>
            {
                var status = get(nameof(CarProcurementOrder.Status));
                if (status == nameof(ProcurementOrderStatus.Rejected)) return "rejected";
                if (status == nameof(ProcurementOrderStatus.Closed)) return "closed";
                var stage = get(nameof(CarProcurementOrder.Stage));
                return stage is nameof(ProcurementStage.Requisition) or nameof(ProcurementStage.Rfq) ? "awaiting" : stage == null ? null : "approved";
            },
            Pending = S("awaiting"), Approved = S("approved"), Rejected = S("rejected"),
            Number = o => ((CarProcurementOrder)o).OrderNumber, Amount = o => ((CarProcurementOrder)o).GrandTotal,
            Url = o => ((CarProcurementOrder)o).Stage == ProcurementStage.Rfq
                ? "/showroom/procurement/stage/rfq_approved" : "/showroom/procurement/stage/requisition_approved",
        },
        Doc<CarSalesContract>("car_sales_contract", "car-showroom", "عقد بيع مركبة", "Vehicle sales contract",
            nameof(CarSalesContract.Status), S(nameof(SalesContractStatus.Draft)),
            S(nameof(SalesContractStatus.Approved), nameof(SalesContractStatus.Allocated), nameof(SalesContractStatus.Delivered), nameof(SalesContractStatus.Invoiced)),
            c => c.ContractNumber, _ => "/showroom/contracts/stage/approvals", amount: c => c.TotalWithVat),

        // ---------- المشتريات ----------
        Doc<MaterialRequisition>("material_requisition", PermissionCatalog.PurchaseRequisitionApprovalScreenId, "طلب شراء", "Purchase requisition",
            nameof(MaterialRequisition.Status), S("pending_approval"), S("approved", "converted_to_po", "converted_to_invoice"),
            r => r.RequisitionNumber, r => r.ContractId is Guid c ? Project(c, "supply") : "/purchases/requisitions",
            rejected: S("rejected"), returned: S("draft"), amount: r => r.TotalEstimatedCost),
        Doc<PurchaseLandedCost>("purchase_landed_cost", "purchase-landed-costs", "سند تكاليف شحن مشتريات", "Purchase landed cost voucher",
            nameof(PurchaseLandedCost.Status), S(nameof(PurchaseLandedCostStatus.Draft)), S(nameof(PurchaseLandedCostStatus.Approved)),
            v => v.VoucherNumber, _ => "/purchases/landed-costs", amount: v => v.GrandTotal),

        // ---------- المقاولات والعقود ----------
        Doc<SiteDailyReport>("site_daily_report", PermissionCatalog.SiteDailyReportsScreenId, "تقرير يومي للموقع", "Site daily report",
            nameof(SiteDailyReport.Status), S(SiteDailyReportStatuses.Submitted), S(SiteDailyReportStatuses.Approved),
            r => Day(r.ReportDate), r => $"/construction/site-reports?contract={r.ContractId}&date={Day(r.ReportDate)}",
            returned: S(SiteDailyReportStatuses.Draft), requester: r => r.SubmittedByUserId),
        Doc<ProjectExecutionPlan>("execution_plan", PermissionCatalog.CommercialContractsScreenId, "خطة تنفيذ مشروع", "Project execution plan",
            nameof(ProjectExecutionPlan.Status), S(ExecutionPlanStatuses.Submitted), S(ExecutionPlanStatuses.Approved),
            p => $"Rev {p.Revision}", p => Project(p.ContractId, "plan"),
            returned: S(ExecutionPlanStatuses.Draft), requester: p => p.SubmittedByUserId),
        Doc<ProjectMaterialPlan>("project_material_plan", PermissionCatalog.CommercialContractsScreenId, "احتياجات مواد المشروع", "Project material requirements",
            nameof(ProjectMaterialPlan.Status), S(ProjectMaterialPlanStatuses.Submitted), S(ProjectMaterialPlanStatuses.Approved),
            _ => null, p => Project(p.ContractId, "materials"), returned: S(ProjectMaterialPlanStatuses.Draft)),
        Doc<ProjectMaterialAdjustment>("project_material_adjustment", PermissionCatalog.CommercialContractsScreenId, "تعديل مواد المشروع", "Project material adjustment",
            nameof(ProjectMaterialAdjustment.Status), S(ProjectMaterialAdjustmentStatuses.Proposed), S(ProjectMaterialAdjustmentStatuses.Approved),
            a => a.VariationNumber, a => Project(a.ContractId, "materials"), rejected: S(ProjectMaterialAdjustmentStatuses.Rejected)),
        Doc<ContractCertificate>("contract_certificate", PermissionCatalog.ContractCertificateApprovalScreenId, "مستخلص", "Progress certificate",
            nameof(ContractCertificate.Status), S(ContractCertificateStatuses.Submitted),
            S(ContractCertificateStatuses.Approved, ContractCertificateStatuses.Invoiced),
            c => c.CertificateNumber, c => Project(c.ContractId, "certificates"),
            rejected: S(ContractCertificateStatuses.Rejected), returned: S(ContractCertificateStatuses.Draft), amount: c => c.NetPayable),
        Doc<ContractVariation>("contract_variation", PermissionCatalog.CommercialContractsScreenId, "أمر تغيير", "Contract variation",
            nameof(ContractVariation.Status), S(ContractVariationStatuses.Submitted), S(ContractVariationStatuses.Approved),
            v => v.VariationNumber, v => Project(v.ContractId, "variations"),
            rejected: S(ContractVariationStatuses.Rejected), returned: S(ContractVariationStatuses.Draft), amount: v => v.ValueChange),
        Doc<EquipmentRentalContract>("equipment_rental", "equipment", "عقد استئجار معدة", "Equipment rental contract",
            nameof(EquipmentRentalContract.Status), S(EquipmentRentalStatuses.Draft), S(EquipmentRentalStatuses.Active, EquipmentRentalStatuses.Closed),
            r => r.ContractNumber, _ => "/construction/equipment?tab=rentals", amount: r => r.Rate),
        Doc<AttendanceGpsPunch>("attendance_gps_punch", "project-attendance", "بصمة حضور خارج الموقع", "Off-site attendance punch",
            nameof(AttendanceGpsPunch.ReviewStatus), S(GpsExceptionReviews.Pending), S(GpsExceptionReviews.Approved),
            p => p.RecordedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), p => Project(p.ContractId, "attendance"),
            rejected: S(GpsExceptionReviews.Rejected)),

        // ---------- الموارد البشرية ----------
        // خدمة الإجازات تُبلغ الموظف بالقرار بنفسها
        Doc<LeaveRequest>("leave_request", "hr", "طلب إجازة", "Leave request",
            nameof(LeaveRequest.Status), S(LeaveStatuses.Pending), S(LeaveStatuses.Approved),
            l => l.RequestNumber, _ => "/hr/leaves",
            rejected: S(LeaveStatuses.Rejected), returned: S(LeaveStatuses.NeedsInfo), notifyRequester: false),
        Doc<PayrollRun>("payroll_run", "hr", "مسير رواتب", "Payroll run",
            nameof(PayrollRun.Status), S("draft"), S("posted"), r => r.RunNumber, _ => "/payroll", amount: r => r.TotalNet),

        // ---------- المخزون ----------
        // خدمة الجرد تُبلغ مقدّمه بالقرار بنفسها (أو محرك السياسات إن انطبقت سياسة)
        Doc<InventoryCountApproval>("inventory_count_approval", "inventory-counts", "اعتماد جرد", "Inventory count approval",
            nameof(InventoryCountApproval.Status), S(nameof(InventoryCountApprovalStatus.Pending)), S(nameof(InventoryCountApprovalStatus.Approved)),
            a => a.ApprovalNumber, a => a.Scope == InventoryCountScope.Vehicles ? "/showroom/inventory-counts/approvals" : "/inventory-counts/approvals",
            rejected: S(nameof(InventoryCountApprovalStatus.Rejected)), amount: a => a.TotalSurplusValue + a.TotalShortageValue,
            requester: a => a.RequestedByUserId, notifyRequester: false),
    };

    private static readonly Dictionary<Type, ApprovalTrackedDocument> ByType = All.ToDictionary(d => d.EntityType);
    private static readonly Dictionary<string, ApprovalTrackedDocument> ByKey = All.ToDictionary(d => d.Key);

    /// <summary>المستند المتابَع لنوع الكيان (أو أحد أسلافه)، أو null.</summary>
    public static ApprovalTrackedDocument? For(Type type)
    {
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            if (ByType.TryGetValue(t, out var doc)) return doc;
        return null;
    }

    public static ApprovalTrackedDocument? Find(string key) => ByKey.GetValueOrDefault(key);
}
