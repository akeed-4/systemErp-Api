namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// نتيجة تهيئة حسابات (العقود أو الجرد الدوري): ما أُنشئ وما أُعيد استخدامه وما بقي على اختيار المستخدم وما تُرك لأنه اختياري
/// وما يحتاج تدخلاً. المعاينة (IsPreview) فحص اكتمال بلا كتابة: ما سيُنشأ حالته missing.
/// </summary>
public class ContractAccountsSetupDto
{
    public List<ContractAccountSetupItemDto> Items { get; set; } = new();
    public bool IsPreview { get; set; }
    public int Created => Items.Count(i => i.Status == ContractAccountSetupStatus.Created);
    public int Reused => Items.Count(i => i.Status == ContractAccountSetupStatus.Reused);
    public int Kept => Items.Count(i => i.Status == ContractAccountSetupStatus.Kept);
    public int Problems => Items.Count(i => i.Status == ContractAccountSetupStatus.Problem);
    public int Skipped => Items.Count(i => i.Status == ContractAccountSetupStatus.Skipped);
    public int Missing => Items.Count(i => i.Status == ContractAccountSetupStatus.Missing);
    /// <summary>كل الحسابات المطلوبة مربوطة وصالحة (لا مشكلة ولا ناقص).</summary>
    public bool Success => Problems == 0 && Missing == 0;
}

public class ContractAccountSetupItemDto
{
    /// <summary>مفتاح الإعداد (مثل ContractAdvancesReceivedAccountCode).</summary>
    public string Role { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;
    /// <summary>created | reused | kept | skipped | missing | problem</summary>
    public string Status { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    /// <summary>سبب المشكلة (حساب الإعداد غير صالح، أكثر من حساب مرشّح، لا أب مناسب) أو إيضاح (الحساب الاختياري، حسابات المواقع).</summary>
    public string? Message { get; set; }
}

public static class ContractAccountSetupStatus
{
    /// <summary>أُنشئ الحساب في الشجرة ورُبط بالإعداد.</summary>
    public const string Created = "created";
    /// <summary>حساب قائم مناسب (بالاسم والنوع) رُبط بالإعداد.</summary>
    public const string Reused = "reused";
    /// <summary>الإعداد محدد مسبقاً (اختيار المستخدم) وصالح: لم يُمس.</summary>
    public const string Kept = "kept";
    /// <summary>لم يُغيَّر شيء: الإعداد القائم غير صالح، أو أكثر من حساب مرشّح، أو لا أب مناسب.</summary>
    public const string Problem = "problem";
    /// <summary>حساب اختياري بلا حساب قائم مناسب: لم يُنشأ (مردودات المشتريات: تُخفَّض المشتريات نفسها).</summary>
    public const string Skipped = "skipped";
    /// <summary>معاينة: الحساب غير مربوط وسيُنشأ عند التهيئة.</summary>
    public const string Missing = "missing";
}
