namespace ERP.Core.Models.Accounting;

/// <summary>سجل مراحل القطعة: من مرحلة إلى أخرى، ومن نفّذ، ومتى، والمدة في المرحلة السابقة. لا يُعدَّل ولا يُحذف.</summary>
public class TailoringStageHistory : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid GarmentId { get; set; }
    public Guid? FromStageId { get; set; }
    public Guid? ToStageId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    /// <summary>منفّذ المرحلة السابقة (أجر القطعة في المرحلة 4).</summary>
    public Guid? EmployeeId { get; set; }
    public int DurationMinutes { get; set; }
    public string? Note { get; set; }
    public string? ChangedBy { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}

public static class TailoringFittingStatuses
{
    public const string Scheduled = "scheduled";
    public const string Done = "done";
    public const string NoShow = "no_show";
    public const string Cancelled = "cancelled";
}

public static class TailoringFittingResults
{
    public const string Approved = "approved";
    public const string Modification = "modification";
}

/// <summary>بروفة قطعة: موعدها ونتيجتها؛ نتيجة «تعديل» تنشئ طلب تعديل مرتبطاً بها.</summary>
public class TailoringFitting : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid GarmentId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string Status { get; set; } = TailoringFittingStatuses.Scheduled;
    public string? Result { get; set; }
    public string? Notes { get; set; }
    public DateTime? DoneAt { get; set; }
    public string? CreatedBy { get; set; }
}

public static class TailoringModificationStatuses
{
    public const string Open = "open";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
}

/// <summary>طلب تعديل على قطعة (من بروفة أو مباشرة). القياس والتصميم الأصليان لا يتغيران؛ التعليمات هنا. إنجازه يعيد القطعة للجودة.
/// تعديل ما بعد التسليم (<see cref="AfterDelivery"/>) لقطعة سُلّمت: مجاني بلا فاتورة ولا تغيير على حالة القطعة أو الأمر، وإنجازه = إعادتها للعميل.</summary>
public class TailoringModification : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid GarmentId { get; set; }
    public Guid? FittingId { get; set; }
    /// <summary>تعديل بعد التسليم (القطعة «مُسلَّمة» وتبقى كذلك).</summary>
    public bool AfterDelivery { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public Guid? AssignedEmployeeId { get; set; }
    public string Status { get; set; } = TailoringModificationStatuses.Open;
    public DateTime? DoneAt { get; set; }
    public string? CreatedBy { get; set; }
}

public static class TailoringDeliveryStatuses
{
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// تسليم قطع جاهزة من أمر (جزئي أو كامل) بمن استلمها وتوقيعه. فاتورته الضريبية لصافي القطع ناقص نصيبها من الدفعة المقدمة (مرجعها
/// <c>tailoring_delivery</c>)، وقيد الاسترداد مدين الدفعات المقدمة / دائن الإيراد، وقيد التكلفة مدين تكلفة الإيرادات / دائن تحت التنفيذ.
/// الإلغاء بمرتجع نظامي وعكس القيدين ويعيد القطع «جاهزة».
/// </summary>
public class TailoringDelivery : BaseEntity
{
    public Guid OrderId { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = TailoringDeliveryStatuses.Posted;
    public int PieceCount { get; set; }
    public string? ReceivedBy { get; set; }
    public string? ReceiverPhone { get; set; }
    /// <summary>توقيع المستلم صورة PNG (data URL).</summary>
    public string? SignatureImage { get; set; }
    public string? Notes { get; set; }
    /// <summary>صافي القطع قبل الضريبة (نصيبها من صافي الأمر).</summary>
    public decimal NetAmount { get; set; }
    /// <summary>المسترد من صافي الدفعة المقدمة.</summary>
    public decimal AdvanceRecovery { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public Guid? RecoveryJournalEntryId { get; set; }
    public decimal CostAmount { get; set; }
    public Guid? CostJournalEntryId { get; set; }
    /// <summary>آخر تسليم للأمر (يُسترد فيه باقي الدفعة وتُنقل باقي التكلفة).</summary>
    public bool IsFinal { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>قطعة في تسليم بنصيبها من الصافي والاسترداد والتكلفة (يبقى بعد الإلغاء للسجل).</summary>
public class TailoringDeliveryPiece : BaseEntity
{
    public Guid DeliveryId { get; set; }
    public Guid GarmentId { get; set; }
    public Guid LineId { get; set; }
    public decimal NetAmount { get; set; }
    public decimal AdvanceRecovery { get; set; }
    public decimal CostAmount { get; set; }
}
