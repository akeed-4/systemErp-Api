namespace ERP.Core.Models.Accounting;

/// <summary>فئة نوع اللباس (للتصفية والتقارير؛ القواعد لا تُبنى عليها).</summary>
public static class TailoringGarmentCategories
{
    public const string Men = "men";
    public const string Women = "women";
    public const string Kids = "kids";
    public const string Uniform = "uniform";
    public const string Other = "other";
    public static bool IsValid(string? v) => v is Men or Women or Kids or Uniform or Other;
}

/// <summary>وحدة القياس: القيم تُحفظ بوحدة القالب كما أُدخلت (ت4).</summary>
public static class TailoringMeasurementUnits
{
    public const string Cm = "cm";
    public const string Inch = "inch";
    public static bool IsValid(string? v) => v is Cm or Inch;
}

/// <summary>طريقة إدخال خيار التصميم.</summary>
public static class TailoringOptionInputTypes
{
    public const string Select = "select";
    public const string Text = "text";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public static bool IsValid(string? v) => v is Select or Text or Number or Boolean;
}

/// <summary>إعداد الخياطة للمنشأة (سطر واحد تفرضه الخدمة).</summary>
public class TailoringSettings : BaseEntity
{
    /// <summary>موقع صرف الأقمشة والمواد الافتراضي.</summary>
    public Guid? DefaultWarehouseId { get; set; }
    /// <summary>مدة التسليم الافتراضية بالأيام لأمر جديد.</summary>
    public int DefaultLeadDays { get; set; } = 7;
    /// <summary>الحد الأدنى للدفعة المقدمة عند التأكيد (نسبة من الإجمالي، 0 = اختيارية).</summary>
    public decimal MinAdvancePercent { get; set; }
    /// <summary>فحص الجودة إلزامي قبل «جاهز للتسليم» (ت14).</summary>
    public bool RequireQualityCheck { get; set; } = true;
    /// <summary>آخر إصدار من الحقول الافتراضية طُبِّق على قالب الثوب (يُستكمل الناقص مرة واحدة لكل إصدار).</summary>
    public int DefaultsVersion { get; set; }
}

/// <summary>
/// نوع لباس (ثوب رجالي، عباية، زي موحد...). الإيراد والضريبة من صنفه الخدمي القائم <see cref="ProductId"/> (إلزامي عند الفوترة)؛
/// حقول القياس وخيارات التصميم تتبعه فلا يُكتب أي خيار في الواجهة.
/// </summary>
public class TailoringGarmentType : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Category { get; set; } = TailoringGarmentCategories.Men;
    public Guid? ProductId { get; set; }
    /// <summary>سعر القطعة الأساسي قبل الضريبة وفروق التصميم والقماش.</summary>
    public decimal BasePrice { get; set; }
    /// <summary>الأمتار التقديرية للقطعة (للتسعير فقط؛ الاستهلاك الفعلي من الصرف).</summary>
    public decimal DefaultFabricMeters { get; set; }
    public string MeasurementUnit { get; set; } = TailoringMeasurementUnits.Cm;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>حقل قياس لنوع لباس. المفتاح ثابت بعد استخدامه لأنه مفتاح القيم المحفوظة في النسخ.</summary>
public class TailoringMeasurementField : BaseEntity
{
    public Guid GarmentTypeId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? GroupName { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    /// <summary>«رقم» (قياس بالوحدة) أو «اختيار» (صفة جسم من قائمة <see cref="ChoicesJson"/>).</summary>
    public string InputType { get; set; } = TailoringOptionInputTypes.Number;
    /// <summary>قيم الاختيار JSON: رمز واسم عربي وإنجليزي (لحقل «اختيار» فقط).</summary>
    public string? ChoicesJson { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>خيار تصميم لنوع لباس (نوع الياقة، الكبك، الجيوب...).</summary>
public class TailoringDesignOption : BaseEntity
{
    public Guid GarmentTypeId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string InputType { get; set; } = TailoringOptionInputTypes.Select;
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>قيمة خيار من نوع «اختيار» بفرق سعرها على القطعة.</summary>
public class TailoringDesignOptionValue : BaseEntity
{
    public Guid OptionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public decimal PriceDelta { get; set; }
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// مرحلة إنتاج قابلة للتهيئة. الإلزامية لا تُتخطى، ومرحلة الجودة تحتاج اعتماد <c>tailoring-quality</c>. الأنواع التي تنطبق عليها
/// <see cref="GarmentTypeIds"/> (معرّفات مفصولة بفاصلة؛ فارغ = كل الأنواع). الأجر الافتراضي يُقترح عند الإسناد ويُثبَّت على العملية.
/// </summary>
public class TailoringStage : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int SortOrder { get; set; }
    public bool IsMandatory { get; set; }
    public bool IsQualityCheck { get; set; }
    public decimal DefaultPieceRate { get; set; }
    public string? GarmentTypeIds { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>امتداد العميل للخياطة (1:1، ت2): التفضيلات والملاحظات فقط؛ بقية البيانات من <see cref="ERP.Core.Models.Shared.Customer"/>.</summary>
public class TailoringCustomerProfile : BaseEntity
{
    public Guid CustomerId { get; set; }
    public string? Preferences { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// ملف قياس للابس واحد (العميل نفسه أو أحد أفراده، ت3) ولنوع لباس. القيم في نسخ لا تُعدَّل <see cref="TailoringMeasurementVersion"/>؛
/// الملف يشير لأحدثها.
/// </summary>
public class TailoringMeasurementProfile : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Guid GarmentTypeId { get; set; }
    public string WearerName { get; set; } = string.Empty;
    public string? Relation { get; set; }
    public int CurrentVersionNo { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>نسخة قياس ثابتة: لا تُعدَّل ولا تُحذف؛ القياس الجديد نسخة جديدة. القيم JSON بمفاتيح حقول القالب.</summary>
public class TailoringMeasurementVersion : BaseEntity
{
    public Guid ProfileId { get; set; }
    public int VersionNo { get; set; }
    public DateTime MeasuredAt { get; set; }
    public Guid? MeasuredByEmployeeId { get; set; }
    public string? MeasuredByName { get; set; }
    public string Unit { get; set; } = TailoringMeasurementUnits.Cm;
    public string ValuesJson { get; set; } = "{}";
    /// <summary>صفات الجسم المختارة JSON: مفتاح الحقل ← رمز القيمة.</summary>
    public string? ChoicesJson { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}
