namespace ERP.Core.DTOs.Accounting;

public class BusinessActivityDto
{
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string? ParentKey { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ChartTemplateAccountDto
{
    public Guid? Id { get; set; }
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
    public AccountCategory Type { get; set; }
    public bool IsGroup { get; set; }
    public string? SemanticKey { get; set; }
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    /// <summary>للقراءة: رقم محجوز يقرؤه النظام مباشرة (لا يُغيَّر نوعه ولا أبوه).</summary>
    public bool IsReserved { get; set; }
}

public class ChartTemplateSummaryDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public ChartTemplateKind Kind { get; set; }
    public string? ActivityKey { get; set; }
    public string? ModuleKey { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int AccountCount { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class ChartTemplateDto : ChartTemplateSummaryDto
{
    public List<ChartTemplateAccountDto> Accounts { get; set; } = new();
}

public class SaveChartTemplateDto
{
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public ChartTemplateKind Kind { get; set; }
    public string? ActivityKey { get; set; }
    public string? ModuleKey { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    /// <summary>حسابات القالب كاملة (تستبدل القائمة السابقة).</summary>
    public List<ChartTemplateAccountDto> Accounts { get; set; } = new();
}

/// <summary>طلب معاينة: النشاط والموديولات، فتُعرض الشجرة الناتجة قبل اعتمادها.</summary>
public class ChartCompositionRequestDto
{
    public string? ActivityKey { get; set; }
    public string[] Modules { get; set; } = Array.Empty<string>();
}

public class ComposedAccountDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
    public AccountCategory Type { get; set; }
    public bool IsGroup { get; set; }
    public int Level { get; set; }
    public string? SemanticKey { get; set; }
    public bool IsRequired { get; set; }
    /// <summary>القوالب التي طلبت الحساب (الدمج يُبقي حساباً واحداً).</summary>
    public List<string> Sources { get; set; } = new();
    /// <summary>الرقم يُولَّد عند التطبيق (الرقم المعروض تقديري على الشجرة الافتراضية).</summary>
    public bool CodeGenerated { get; set; }
}

public class ChartCompositionDto
{
    public List<string> Templates { get; set; } = new();
    public List<ComposedAccountDto> Accounts { get; set; } = new();
    /// <summary>أخطاء تمنع التطبيق (أب مفقود، تعارض نوع، رقم محجوز لغير غرضه، تكرار مفتاح وظيفي...).</summary>
    public List<string> Errors { get; set; } = new();
    public bool IsValid => Errors.Count == 0;
}

public enum ChartSetupItemStatus
{
    /// <summary>موجود في دليل المنشأة (بالمفتاح أو بالرقم) ولا يُمس.</summary>
    Existing = 1,
    /// <summary>سيُنشأ (معاينة) أو أُنشئ (تنفيذ).</summary>
    Created = 2,
    /// <summary>حساب قائم رُبط بالمفتاح الوظيفي.</summary>
    Bound = 3,
    /// <summary>تعذّر: الرقم مستخدم لحساب من نوع آخر، أو الأب غير موجود... يحتاج تدخلاً.</summary>
    Problem = 4,
}

public class ChartSetupItemDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
    public AccountCategory Type { get; set; }
    public string? SemanticKey { get; set; }
    public bool IsRequired { get; set; }
    public ChartSetupItemStatus Status { get; set; }
    public string? Message { get; set; }
    public List<string> Sources { get; set; } = new();
}

public class ChartSetupReportDto
{
    public bool IsPreview { get; set; }
    public List<string> Templates { get; set; } = new();
    public List<ChartSetupItemDto> Items { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public int Created => Items.Count(i => i.Status == ChartSetupItemStatus.Created);
    public int Bound => Items.Count(i => i.Status == ChartSetupItemStatus.Bound);
    public int Existing => Items.Count(i => i.Status == ChartSetupItemStatus.Existing);
    public int Problems => Items.Count(i => i.Status == ChartSetupItemStatus.Problem);
    /// <summary>كل الحسابات الإلزامية موجودة ومربوطة، ولا أخطاء دمج.</summary>
    public bool Complete => Errors.Count == 0 && !Items.Any(i => i.IsRequired && i.Status == ChartSetupItemStatus.Problem);
}

/// <summary>حالة دليل المنشأة: النشاط والموديولات المطبَّقة وما ينتظر التطبيق.</summary>
public class ChartSetupStatusDto
{
    /// <summary>منشأة أُسست قبل القوالب (لا سجل تأسيس): دليلها كما هو.</summary>
    public bool IsLegacy { get; set; }
    public string? ActivityKey { get; set; }
    public ChartProvisioningStatus? Status { get; set; }
    public string? LastError { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> AppliedTemplates { get; set; } = new();
    public List<string> AppliedModules { get; set; } = new();
    public List<string> LicensedModules { get; set; } = new();
    /// <summary>موديولات مرخّصة لم تُطبَّق قوالبها بعد.</summary>
    public List<string> PendingModules { get; set; } = new();
}

public class ApplyChartSetupDto
{
    /// <summary>نشاط جديد (اختياري): يُحفظ ويُعرض ما يضيفه قالبه، ولا يُنشأ شيء إلا بالتنفيذ.</summary>
    public string? ActivityKey { get; set; }
    /// <summary>true = تطبيق قالب النشاط (بعد مراجعة المعاينة)؛ false = الموديولات الناقصة فقط.</summary>
    public bool IncludeActivity { get; set; }
}

public class AccountSemanticKeyDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? DefaultCode { get; set; }
    /// <summary>مكان الربط: setting (إعدادات الحسابات) | binding (ربط عام) | reserved (رقم ثابت يقرؤه النظام).</summary>
    public string Store { get; set; } = string.Empty;
    public AccountCategory[] Types { get; set; } = Array.Empty<AccountCategory>();
}

/// <summary>الحساب الفعلي المربوط بمفتاح وظيفي في المنشأة الحالية.</summary>
public class AccountBindingDto
{
    public string SemanticKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Store { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
}

public class SaveAccountBindingDto
{
    public string SemanticKey { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
}
