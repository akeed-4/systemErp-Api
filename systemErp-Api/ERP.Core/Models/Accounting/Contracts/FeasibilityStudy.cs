namespace ERP.Core.Models.Accounting;

/// <summary>
/// مراحل السجل الواحد قبل أن يصير عقداً: دراسة جدوى (جدول الكميات وتسعيره وتحليله) ← خطة تنفيذ معتمدة ← عقد (بإجراء «إنشاء العقد»
/// يُعطى رقم العقد ويدخل دورة العقد). قبل العقد لا يظهر في قوائم العقود والمشاريع ولا يخرج من المسودة، ولا رقم عقد له.
/// </summary>
public static class FeasibilityStatuses
{
    public const string Study = "study";
    /// <summary>خطة التنفيذ اعتُمدت ونُسخت الدراسة والخطة مجمّدتين؛ ينتظر «إنشاء العقد».</summary>
    public const string PlanApproved = "plan_approved";
    /// <summary>صار عقداً (برقمه)؛ يبقى رقم الدراسة ونسخها المرجعية.</summary>
    public const string Contracted = "contracted";

    /// <summary>قبل العقد: دراسة أو خطة معتمدة.</summary>
    public static bool IsPreContract(string? status) => status is Study or PlanApproved;
}
