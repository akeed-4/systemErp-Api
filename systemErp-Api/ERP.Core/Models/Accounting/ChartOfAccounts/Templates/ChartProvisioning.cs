namespace ERP.Core.Models.Accounting;

public enum ChartProvisioningStatus
{
    /// <summary>لم يُطبَّق بعد (أو طُلب تطبيق لم يكتمل).</summary>
    Pending = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// حالة تأسيس الدليل للمنشأة (سجل واحد لكل منشأة، في قاعدتها هي مع حساباتها فيُحفظ في معاملتها نفسها). المنشأة بلا سجل
/// منشأة قديمة أُسست قبل القوالب ولا يمسّها المحرك إلا بطلب صريح.
/// </summary>
public class ChartProvisioning : BaseEntity
{
    public string? ActivityKey { get; set; }
    /// <summary>القوالب المطبَّقة بإصداراتها (key@version مفصولة بفاصلة).</summary>
    public string AppliedTemplates { get; set; } = string.Empty;
    /// <summary>الموديولات التي طُبِّقت قوالبها (مفاتيح PlatformModules مفصولة بفاصلة): الفرق عن الاشتراك = ما ينتظر التطبيق.</summary>
    public string AppliedModules { get; set; } = string.Empty;
    public ChartProvisioningStatus Status { get; set; } = ChartProvisioningStatus.Pending;
    public string? LastError { get; set; }
    public int AccountsCreated { get; set; }
    public int BindingsCreated { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// ربط مفتاح وظيفي بحساب المنشأة للمفاتيح التي لا عمود لها في <see cref="AccountingSettings"/>. يُخزَّن معرّف الحساب (لا رقمه)
/// فلا يتأثر بتغيير الرقم أو الاسم. فريد لكل (منشأة، مفتاح).
/// </summary>
public class AccountBinding : BaseEntity
{
    public string SemanticKey { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
}
