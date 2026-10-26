namespace ERP.Core.Models.Shared;

/// <summary>
/// حالة من حالات العقد (draft, review, approval, active, initial_acceptance, closed, suspended, cancelled).
/// الانتقال بينها من جدول <see cref="ContractTransition"/> لا من ترتيبها؛ والرقم للعرض فقط.
/// </summary>
public sealed class ContractStageDefinition
{
    public const string Workflow = "contract";

    public const string Draft = "draft";
    public const string Review = "review";
    public const string Approval = "approval";
    public const string Active = "active";
    public const string InitialAcceptance = "initial_acceptance";
    public const string Closed = "closed";
    public const string Suspended = "suspended";
    public const string Cancelled = "cancelled";

    public string Id { get; set; } = string.Empty;
    public string WorkflowType { get; set; } = Workflow;
    public int Number { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>اسم بكلمة واحدة لبطاقة المرحلة (والكامل في التلميح).</summary>
    public string ShortNameAr { get; set; } = string.Empty;
    public string ShortNameEn { get; set; } = string.Empty;
    public string DescAr { get; set; } = string.Empty;
    public string DescEn { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string BadgeColor { get; set; } = string.Empty;
    public string HeaderColor { get; set; } = string.Empty;
    /// <summary>pre_signing | execution | closing | side (موقوف/ملغي خارج المسار).</summary>
    public string Phase { get; set; } = string.Empty;
    /// <summary>المدة المستهدفة بالأيام للبقاء في الحالة؛ تجاوزها = متأخر. null = بلا مدة.</summary>
    public int? SlaDays { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>الإجراءات المسموحة على العقد ومستنداته وهو في هذه الحالة (رموز ScreenAction مفصولة بفواصل)؛ الانتقال من ContractTransition.</summary>
    public string AllowedActions { get; set; } = string.Empty;
}
