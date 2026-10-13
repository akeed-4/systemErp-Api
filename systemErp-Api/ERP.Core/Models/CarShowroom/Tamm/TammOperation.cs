namespace ERP.Core.Models.CarShowroom;

public enum TammOperationType
{
    TransferToShowroom = 1,     // نقل ملكية للمعرض
    Hold = 2,                   // تحفظ
    AutoHold = 3,               // تحفظ تلقائي
    ViolationsInquiry = 4,      // استعلام مخالفات
    RegistrationIssue = 5,      // إصدار استمارة
    RegistrationRenewal = 6,    // تجديد استمارة
    InternalAuthorization = 7,  // تفويض داخلي
    ExternalAuthorization = 8,  // تفويض خارجي
    ActualDriver = 9,           // قائد فعلي
    TransferToBuyer = 10        // نقل ملكية للمشتري
}

public enum TammOperationStatus
{
    Pending = 1,
    Success = 2,
    Failed = 3,
    /// <summary>لم تُعرف نتيجتها (انقطاع، مهلة): لا أثر مالي حتى تُحسم يدوياً.</summary>
    Unknown = 4
}

/// <summary>
/// عملية على منصة «تم» لسيارة. سجل بسيط تُحدَّث حالته من نقطة تحديث الحالة (الربط الفعلي مع تم خارج النطاق).
/// النجاح ينشئ مصروفها الفعلي مرة واحدة؛ الفشل والمجهول لا ينشئان شيئاً.
/// </summary>
public class TammOperation : BaseEntity
{
    public string OperationNumber { get; set; } = string.Empty;
    public TammOperationType Type { get; set; }
    public TammOperationStatus Status { get; set; } = TammOperationStatus.Pending;
    /// <summary>السيارة إن كانت في النظام؛ فارغ لاستعلام على سيارة لم تُشترَ بعد (يكفي رقم الشاسيه).</summary>
    public Guid? VehicleId { get; set; }
    public string? Vin { get; set; }
    /// <summary>عقد البيع (لنقل الملكية للمشتري).</summary>
    public Guid? ContractId { get; set; }
    /// <summary>رسم العملية بالعملة الأساسية.</summary>
    public decimal Fee { get; set; }
    /// <summary>مرجع العملية في تم.</summary>
    public string? Reference { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    /// <summary>حُسمت يدوياً بعد أن كانت مجهولة النتيجة.</summary>
    public bool ResolvedManually { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Notes { get; set; }
}
