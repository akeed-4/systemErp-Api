namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>
/// متابعة توريد مواد المشروع لكل صنف: المطلوب في الاحتياجات المعتمدة (دراسة الجدوى) بوحدة الصنف، وما طُلب شراؤه، وما اشتُري،
/// وما سُلِّم للمشروع، والمتبقي للطلب وللتسليم.
/// </summary>
public class ProjectMaterialTrackingDto
{
    public Guid ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? ProjectLocation { get; set; }
    public Guid? WarehouseId { get; set; }
    /// <summary>حالة خطة الاحتياجات: none | draft | submitted | approved. المطلوب من المعتمدة وحدها.</summary>
    public string PlanStatus { get; set; } = "none";
    /// <summary>مواد في الخطة غير مربوطة بصنف مخزني (لا تُطلب ولا تُصرف حتى تُربط).</summary>
    public int UnlinkedMaterials { get; set; }
    public List<ProjectMaterialTrackingLineDto> Lines { get; set; } = new();
    /// <summary>مواد معتمدة لا تُحسب تغطيتها (بلا صنف أو بلا تحويل وحدة)، بتنبيهها.</summary>
    public List<ProjectMaterialUnverifiableDto> Unverifiable { get; set; } = new();
    public ProjectMaterialTrackingTotalsDto Totals { get; set; } = new();
}

public class ProjectMaterialTrackingLineDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    /// <summary>الكمية المطلوبة في دراسة الجدوى (الاحتياجات المعتمدة) بوحدة الصنف.</summary>
    public decimal RequiredQuantity { get; set; }
    /// <summary>طُلب شراؤه في طلبات شراء المشروع (غير المرفوضة).</summary>
    public decimal RequestedQuantity { get; set; }
    /// <summary>اشتُري بفواتير شراء مرحّلة محوّلة من طلبات المشروع.</summary>
    public decimal PurchasedQuantity { get; set; }
    /// <summary>سُلِّم للمشروع بمستندات صرف مرحّلة.</summary>
    public decimal DeliveredQuantity { get; set; }
    /// <summary>أُعيد من الموقع للمستودع (يُطرح من المسلَّم أعلاه)، وهُدر أو تلف في الموقع.</summary>
    public decimal ReturnedQuantity { get; set; }
    public decimal WastedQuantity { get; set; }
    /// <summary>المستهلك في التقارير اليومية المعتمدة (محوّلاً لوحدة الصنف).</summary>
    public decimal ConsumedQuantity { get; set; }
    /// <summary>عهدة الموقع = المسلَّم الصافي − الهدر − المستهلك (سالب = استهلاك مسجَّل أكثر من المسلَّم).</summary>
    public decimal CustodyQuantity { get; set; }
    public decimal RemainingToRequest { get; set; }
    public decimal RemainingToDeliver { get; set; }
    /// <summary>الرصيد في مستودع المشروع (أو مستودع الطلب).</summary>
    public decimal WarehouseStock { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>صنف خارج الاحتياجات المعتمدة (طُلب أو صُرف دون أن يكون مطلوباً).</summary>
    public bool OutsidePlan { get; set; }

    // ---- التغطية (كل كمية في مرحلة واحدة من الدورة فلا تُحسب مرتين) ----
    /// <summary>في طلبات شراء لم تُحوَّل لفاتورة بعد (مسودة أو بانتظار الاعتماد أو معتمدة).</summary>
    public decimal PendingRequestQuantity { get; set; }
    /// <summary>منها المعتمد (جاهز للتحويل لفاتورة شراء).</summary>
    public decimal ApprovedRequestQuantity { get; set; }
    /// <summary>
    /// مطلوب من المورد ولم يُستلم: فواتير شراء مسودة محوّلة من طلبات المشروع (لا خطوة أمر شراء للمشروع؛ الطلب يُحوَّل لفاتورة).
    /// الطلب المحوَّل لا يُعدّ في <see cref="PendingRequestQuantity"/>.
    /// </summary>
    public decimal OrderedQuantity { get; set; }
    /// <summary>استُلم بفواتير شراء مرحّلة من طلبات المشروع (= <see cref="PurchasedQuantity"/>): داخل رصيد المستودع أو سُلِّم، فلا يُطرح مرة أخرى.</summary>
    public decimal ReceivedQuantity { get; set; }
    /// <summary>صافي ما يُشترى = max(0, المتبقي للتسليم − رصيد المستودع − المعلّق في الطلبات − المطلوب غير المستلم).</summary>
    public decimal NetToPurchase { get; set; }
    /// <summary>الكمية المقترحة لطلب شراء جديد = min(صافي الشراء، المتبقي للطلب) — حد السرفر للطلب.</summary>
    public decimal SuggestedRequestQuantity { get; set; }
    /// <summary>شراء زائد: المعلّق والمطلوب أكثر مما ينقص بعد الرصيد.</summary>
    public decimal ExcessPurchaseQuantity { get; set; }
    /// <summary><see cref="MaterialCoverageStatuses"/>: الرصيد الحالي مقابل المتبقي للتسليم.</summary>
    public string CoverageStatus { get; set; } = MaterialCoverageStatuses.Unverifiable;
    public List<ProjectSupplyAlertDto> Alerts { get; set; } = new();
}

/// <summary>حالة تغطية المادة من رصيد المستودع.</summary>
public static class MaterialCoverageStatuses
{
    public const string Full = "full";
    public const string Partial = "partial";
    public const string None = "none";
    /// <summary>لا يُحكم: الاحتياجات غير معتمدة، أو المادة بلا صنف مخزني أو بلا تحويل وحدة.</summary>
    public const string Unverifiable = "unverifiable";
}

/// <summary>تنبيه تغطية: الرمز يحدد السبب والإجراء المقترح (تترجمه الواجهة)، والكمية بوحدة الصنف أو المادة.</summary>
public class ProjectSupplyAlertDto
{
    public string Code { get; set; } = string.Empty;
    /// <summary>info | warning | critical</summary>
    public string Severity { get; set; } = "info";
    public decimal Quantity { get; set; }
}

/// <summary>مادة معتمدة لا تُحسب تغطيتها: بلا صنف مخزني، أو وحدتها لا تتحول لوحدة الصنف.</summary>
public class ProjectMaterialUnverifiableDto
{
    public Guid? ResourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public Guid? ProductId { get; set; }
    public string? ProductUnit { get; set; }
    public ProjectSupplyAlertDto Alert { get; set; } = new();
}

public class ProjectMaterialTrackingTotalsDto
{
    public int Items { get; set; }
    public decimal RequiredValue { get; set; }
    public decimal DeliveredValue { get; set; }
    /// <summary>نسبة التسليم بالقيمة (0-100).</summary>
    public decimal DeliveredPercent { get; set; }
    public int FullyDelivered { get; set; }
    public int PendingRequest { get; set; }
}

public class ProjectMaterialIssueDocDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    /// <summary>للعرض والطباعة: رقم العقد واسم المشروع وموقعه والعميل واسم المستودع.</summary>
    public string? ContractNumber { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectLocation { get; set; }
    public string? PartyName { get; set; }
    public string? WarehouseName { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? WarehouseId { get; set; }
    public string ReceiverType { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public Guid? ReceiverEmployeeId { get; set; }
    public Guid? ReceiverSupplierId { get; set; }
    public Guid? RequisitionId { get; set; }
    public string Status { get; set; } = "draft";
    /// <summary>issue | return | waste</summary>
    public string Kind { get; set; } = "issue";
    public decimal TotalCost { get; set; }
    public Guid? JournalEntryId { get; set; }
    /// <summary>الحساب المدين عند الترحيل (مواد مشاريع تحت التنفيذ)؛ فارغ لمستند قديم رُحِّل مصروفاً أو بجرد دوري.</summary>
    public string? DebitAccountCode { get; set; }
    public string? Notes { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public string? CancelReason { get; set; }
    public List<ProjectMaterialIssueLineDto> Lines { get; set; } = new();
}

public class ProjectMaterialIssueLineDto
{
    public Guid Id { get; set; }
    public Guid? BoqItemId { get; set; }
    public string? BoqLineCode { get; set; }
    public string? BoqDescription { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? Notes { get; set; }
    /// <summary>الإرجاع والهدر: سطر التسليم المصدر.</summary>
    public Guid? SourceLineId { get; set; }
    /// <summary>سطر التسليم: المسحوب منه بإرجاع أو هدر مرحّل.</summary>
    public decimal WithdrawnQuantity { get; set; }
    /// <summary>المختار من السطر في مستخلصات غير ملغاة.</summary>
    public decimal CertifiedQuantity { get; set; }
    // للقراءة (أعمدة المستند): من متابعة المشروع وقت القراءة، دون هذا المستند
    public decimal RequiredQuantity { get; set; }
    public decimal DeliveredBefore { get; set; }
    public decimal RemainingBefore { get; set; }
}

public class SaveProjectMaterialIssueDto
{
    public DateTime? Date { get; set; }
    public string? ManualNumber { get; set; }
    public Guid? WarehouseId { get; set; }
    public string ReceiverType { get; set; } = "site_engineer";
    public string ReceiverName { get; set; } = string.Empty;
    public Guid? ReceiverEmployeeId { get; set; }
    public Guid? ReceiverSupplierId { get; set; }
    public Guid? RequisitionId { get; set; }
    public string? Notes { get; set; }
    /// <summary>issue (افتراضي) | return | waste.</summary>
    public string? Kind { get; set; }
    /// <summary>ترحيل مباشر بعد الحفظ.</summary>
    public bool Post { get; set; }
    public List<SaveProjectMaterialIssueLineDto> Lines { get; set; } = new();
}

public class SaveProjectMaterialIssueLineDto
{
    /// <summary>الإرجاع والهدر: سطر التسليم المرحّل المصدر (الصنف والبند والتكلفة منه).</summary>
    public Guid? SourceLineId { get; set; }
    /// <summary>بند جدول الكميات (سطر قياس في العقد نفسه)، إلزامي للتسليم.</summary>
    public Guid? BoqItemId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

/// <summary>صف قائمة أذونات التسليم لكل المشاريع (في نطاق وصول المستخدم إلى العقود)، بترقيم وفلترة وفرز من السرفر.</summary>
public class ProjectMaterialIssueRowDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string ReceiverType { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public int LineCount { get; set; }
    public string Kind { get; set; } = "issue";
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// مواد كل بند في جدول الكميات لكل صنف: المخطط (الاحتياجات المعتمدة على البند بوحدة الصنف)، والمسلَّم بأذونات مرحّلة وتكلفته الفعلية،
/// والمتبقي، وما اختير في المستخلصات غير الملغاة (والمفوتر منها) بتكلفته، والرصيد تحت التنفيذ (المسلَّم غير المفوتر).
/// </summary>
public class ProjectBoqMaterialLineDto
{
    public Guid ContractItemId { get; set; }
    public string? LineCode { get; set; }
    public string BoqDescription { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal PlannedQuantity { get; set; }
    public decimal DeliveredQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal DeliveredCost { get; set; }
    /// <summary>المختار في مستخلصات غير ملغاة.</summary>
    public decimal CertifiedQuantity { get; set; }
    public decimal CertifiedCost { get; set; }
    /// <summary>المفوتر منه (اعتُرفت تكلفته).</summary>
    public decimal InvoicedCost { get; set; }
    /// <summary>تكلفة المسلَّم غير المفوتر بعد (ما يبقى في مواد تحت التنفيذ لهذا البند).</summary>
    public decimal OutstandingCost { get; set; }
    /// <summary>المسلَّم تجاوز المخطط على هذا البند (تنبيه؛ الحد الملزم هو المطلوب للصنف في دراسة الجدوى).</summary>
    public bool OverPlanned { get; set; }
}
