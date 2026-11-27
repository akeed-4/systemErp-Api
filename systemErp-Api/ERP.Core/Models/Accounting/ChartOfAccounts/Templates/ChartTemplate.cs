namespace ERP.Core.Models.Accounting;

public enum ChartTemplateKind
{
    /// <summary>الحسابات المشتركة لكل المنشآت (يُطبَّق دائماً).</summary>
    Base = 1,
    /// <summary>حسابات نشاط رئيسي (BusinessActivity).</summary>
    Activity = 2,
    /// <summary>حسابات موديول مرخّص في الاشتراك (PlatformModules).</summary>
    Module = 3,
}

/// <summary>
/// قالب دليل محاسبي (على مستوى المنصة، في القاعدة المشتركة وحدها، بلا مرشّح منشأة): مصدر مركزي واحد تُنسخ منه حسابات كل منشأة
/// عند تأسيسها أو تفعيل موديول لها، أياً كانت قاعدتها. لا يُقرأ أثناء العمليات اليومية: بعد التأسيس تتعامل العمليات مع حسابات المنشأة.
/// </summary>
public class ChartTemplate
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public ChartTemplateKind Kind { get; set; }
    /// <summary>نشاط القالب (Kind = Activity).</summary>
    public string? ActivityKey { get; set; }
    /// <summary>موديول القالب (Kind = Module).</summary>
    public string? ModuleKey { get; set; }
    /// <summary>يزيد مع كل تعديل على القالب أو حساباته؛ المنشأة تحفظ الإصدار الذي طُبِّق عليها.</summary>
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }

    public List<ChartTemplateAccount> Accounts { get; set; } = new();
}

/// <summary>حساب في قالب: يطابق نموذج <see cref="Account"/> (الرقم والأب بالرمز، النوع، رئيسي/فرعي).</summary>
public class ChartTemplateAccount
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    /// <summary>الرقم الافتراضي. فارغ = يُرقَّم عند التطبيق تالياً لإخوته تحت الأب (للحسابات الوظيفية في الموديولات).</summary>
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>رقم الحساب الأب (من هذا القالب أو قالب يسبقه في الدمج أو شجرة المنشأة). فارغ = جذر.</summary>
    public string? ParentCode { get; set; }
    public AccountCategory Type { get; set; }
    public bool IsGroup { get; set; }
    /// <summary>المفتاح الوظيفي الثابت (مثل Construction.WorkInProgress): به تُعرف وظيفة الحساب لا برقمه ولا باسمه.</summary>
    public string? SemanticKey { get; set; }
    /// <summary>إلزامي: لا يكتمل التأسيس بدونه. الاختياري يُنشأ مع القالب ويمكن حذفه منه.</summary>
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
