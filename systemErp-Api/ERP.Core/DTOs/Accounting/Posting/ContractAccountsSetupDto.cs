namespace ERP.Core.DTOs.Accounting;

/// <summary>نتيجة تهيئة حسابات العقود: ما أُنشئ وما أُعيد استخدامه وما بقي على اختيار المستخدم وما يحتاج تدخلاً.</summary>
public class ContractAccountsSetupDto
{
    public List<ContractAccountSetupItemDto> Items { get; set; } = new();
    public int Created => Items.Count(i => i.Status == ContractAccountSetupStatus.Created);
    public int Reused => Items.Count(i => i.Status == ContractAccountSetupStatus.Reused);
    public int Kept => Items.Count(i => i.Status == ContractAccountSetupStatus.Kept);
    public int Problems => Items.Count(i => i.Status == ContractAccountSetupStatus.Problem);
}

public class ContractAccountSetupItemDto
{
    /// <summary>مفتاح الإعداد (مثل ContractAdvancesReceivedAccountCode).</summary>
    public string Role { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;
    /// <summary>created | reused | kept | problem</summary>
    public string Status { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    /// <summary>سبب المشكلة (حساب الإعداد غير صالح، أكثر من حساب مرشّح، لا أب مناسب).</summary>
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
}
