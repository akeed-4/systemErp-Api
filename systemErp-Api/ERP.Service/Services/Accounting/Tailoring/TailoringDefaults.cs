using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الافتراضيات التي تُزرع عند أول فتح للوحدة: قالب الثوب السعودي الرجالي (حقول القياس وخيارات التصميم) ومراحل الإنتاج الثمانية.
/// كلها بيانات قابلة للتعديل من الإعدادات؛ لا شيء منها مكتوب في الواجهة.
/// </summary>
internal static class TailoringDefaults
{
    /// <summary>إصدار الحقول الافتراضية: رفعه يستكمل الناقص في قالب الثوب لدى المنشآت القائمة مرة واحدة.</summary>
    public const int Version = 2;
    public const string ThobeCode = "THOBE-M";

    private const string GLength = "الأطوال", GShoulder = "الكتف والظهر", GChest = "الصدر والجذع", GArm = "الإبط", GSleeve = "الأكمام",
        GCuff = "الكبك", GCollar = "الرقبة والياقة", GPlacket = "فتحة الصدر (الجبزور)", GPocket = "الجيوب", GBody = "صفات الجسم";

    private sealed record Field(string Key, string Ar, string En, string Group, bool Required, decimal? Min = null, decimal? Max = null,
        (string Code, string Ar, string En)[]? Choices = null);

    /// <summary>
    /// قياسات الثوب الجاهز (لا مقاس الجسم مع وسع) بالسنتيمتر، والحدود تنبيه على أخطاء الكتابة. صفات الجسم «اختيار» لأن الخياط يعدّل بها القص.
    /// مفاتيح الإصدار الأول باقية كما هي لأن النسخ المحفوظة تشير إليها.
    /// </summary>
    private static readonly Field[] ThobeFields =
    {
        new("length", "طول الثوب من الأمام", "Front Length", GLength, true, 50, 175),
        new("length_back", "طول الثوب من الخلف", "Back Length", GLength, false, 50, 180),
        new("side_length", "طول الجنب", "Side Length", GLength, false, 30, 150),
        new("shoulder", "عرض الكتف", "Shoulder Width", GShoulder, true, 25, 75),
        new("shoulder_drop", "نزول الكتف", "Shoulder Drop", GShoulder, false, 0, 12),
        new("back_width", "عرض الظهر", "Back Width", GShoulder, false, 25, 75),
        new("chest", "محيط الصدر", "Chest", GChest, true, 50, 200),
        new("front_chest_width", "عرض الصدر من الأمام", "Front Chest Width", GChest, false, 20, 80),
        new("belly", "محيط البطن", "Belly", GChest, false, 50, 220),
        new("waist", "الوسط (الخصر)", "Waist", GChest, false, 45, 220),
        new("hip", "الورك", "Hip", GChest, false, 50, 220),
        new("bottom_width", "وسع الذيل", "Bottom Width", GChest, false, 60, 300),
        new("armhole", "حردة الإبط", "Armhole", GArm, false, 25, 90),
        new("armhole_depth", "نزول الإبط", "Armhole Depth", GArm, false, 10, 45),
        new("sleeve_length", "طول الكم", "Sleeve Length", GSleeve, true, 20, 90),
        new("bicep", "محيط العضد", "Bicep", GSleeve, false, 15, 75),
        new("elbow", "محيط الكوع", "Elbow", GSleeve, false, 15, 65),
        new("sleeve_width", "وسع فتحة الكم", "Sleeve Opening", GSleeve, false, 10, 55),
        new("wrist", "المعصم", "Wrist", GSleeve, false, 10, 40),
        new("cuff", "طول الكبك", "Cuff Length", GCuff, false, 2, 25),
        new("cuff_width", "عرض الكبك", "Cuff Width", GCuff, false, 10, 45),
        new("neck", "محيط الرقبة", "Neck", GCollar, true, 25, 65),
        new("collar_height", "ارتفاع الياقة", "Collar Height", GCollar, false, 0.5m, 12),
        new("collar_width", "عرض الياقة", "Collar Width", GCollar, false),
        new("placket_length", "طول فتحة الصدر", "Placket Length", GPlacket, false, 5, 60),
        new("placket_width", "عرض فتحة الصدر", "Placket Width", GPlacket, false, 1, 12),
        new("chest_pocket_drop", "نزول جيب الصدر من الكتف", "Chest Pocket Drop", GPocket, false, 5, 50),
        new("chest_pocket_width", "عرض جيب الصدر", "Chest Pocket Width", GPocket, false, 5, 25),
        new("chest_pocket_length", "طول جيب الصدر", "Chest Pocket Length", GPocket, false, 5, 25),
        new("side_pocket_opening", "طول فتحة الجيب الجانبي", "Side Pocket Opening", GPocket, false, 8, 35),
        new("side_pocket_drop", "نزول الجيب الجانبي من الإبط", "Side Pocket Drop", GPocket, false, 0, 60),
        new("shoulder_slope", "ميل الكتفين", "Shoulder Slope", GBody, false, Choices: new[]
        {
            ("even", "متساويان", "Even"), ("right_low", "الأيمن أنزل", "Right Lower"), ("left_low", "الأيسر أنزل", "Left Lower"),
        }),
        new("belly_shape", "البطن", "Belly Shape", GBody, false, Choices: new[] { ("normal", "عادي", "Normal"), ("prominent", "بارز", "Prominent") }),
        new("back_posture", "الظهر", "Back Posture", GBody, false, Choices: new[] { ("straight", "مستقيم", "Straight"), ("curved", "منحنٍ", "Curved") }),
        new("chest_shape", "الصدر", "Chest Shape", GBody, false, Choices: new[] { ("normal", "عادي", "Normal"), ("prominent", "بارز", "Prominent") }),
    };

    /// <summary>أسماء الإصدار الأول ومجموعاته: الحقل الذي لم يعدّله المستخدم يُنقل لاسم الإصدار الحالي ومجموعته.</summary>
    private static readonly Dictionary<string, (string Ar, string Group)> V1 = new()
    {
        ["length"] = ("طول الثوب", "الجسم"), ["shoulder"] = ("عرض الكتف", "الجسم"), ["chest"] = ("الصدر", "الجسم"),
        ["waist"] = ("الوسط", "الجسم"), ["hip"] = ("الورك", "الجسم"), ["side_length"] = ("طول الجنب", "الجسم"),
        ["bottom_width"] = ("وسع الذيل", "الجسم"), ["sleeve_length"] = ("طول الكم", "الأكمام"), ["sleeve_width"] = ("وسع الكم", "الأكمام"),
        ["cuff"] = ("الكبك", "الأكمام"), ["wrist"] = ("المعصم", "الأكمام"), ["neck"] = ("الرقبة", "الياقة"),
        ["collar_height"] = ("ارتفاع الياقة", "الياقة"), ["collar_width"] = ("عرض الياقة", "الياقة"),
    };

    private static readonly (string Key, string Ar, string En, string Input, bool Required, (string Code, string Ar, string En, decimal Price)[] Values)[] ThobeOptions =
    {
        ("collar_type", "نوع الياقة", "Collar Type", TailoringOptionInputTypes.Select, true, new[]
        {
            ("royal", "ياقة ملكية", "Royal Collar", 0m), ("plain", "ياقة سادة", "Plain Collar", 0m),
            ("japanese", "ياقة يابانية", "Japanese Collar", 0m), ("round", "ياقة مدورة", "Round Collar", 0m),
        }),
        ("sleeve_style", "نوع الكم", "Sleeve Style", TailoringOptionInputTypes.Select, true, new[]
        {
            ("plain", "كم سادة", "Plain Sleeve", 0m), ("cuffed", "كم بكبك", "Cuffed Sleeve", 0m),
        }),
        ("cuff_style", "شكل الكبك", "Cuff Style", TailoringOptionInputTypes.Select, false, new[]
        {
            ("square", "مربع", "Square", 0m), ("round", "مدور", "Round", 0m), ("angled", "مشطوف", "Angled", 0m),
        }),
        ("pockets", "الجيوب", "Pockets", TailoringOptionInputTypes.Select, true, new[]
        {
            ("chest_side", "جيب صدر وجيوب جانبية", "Chest & Side Pockets", 0m), ("chest", "جيب صدر فقط", "Chest Pocket Only", 0m),
            ("side", "جيوب جانبية فقط", "Side Pockets Only", 0m), ("none", "بدون جيوب", "No Pockets", 0m),
        }),
        ("front_opening", "فتحة الصدر", "Front Opening", TailoringOptionInputTypes.Select, true, new[]
        {
            ("hidden", "مخفية", "Hidden Placket", 0m), ("visible", "ظاهرة", "Visible Placket", 0m), ("zipper", "سحاب", "Zipper", 0m),
        }),
        ("buttons", "الأزرار", "Buttons", TailoringOptionInputTypes.Select, false, new[]
        {
            ("regular", "أزرار عادية", "Regular Buttons", 0m), ("hidden", "أزرار مخفية", "Hidden Buttons", 0m), ("snap", "كبسات", "Snaps", 0m),
        }),
        ("embroidery", "التطريز", "Embroidery", TailoringOptionInputTypes.Select, false, new[]
        {
            ("none", "بدون تطريز", "No Embroidery", 0m), ("collar", "تطريز الياقة", "Collar Embroidery", 0m),
            ("cuff", "تطريز الكبك", "Cuff Embroidery", 0m), ("collar_cuff", "تطريز الياقة والكبك", "Collar & Cuff Embroidery", 0m),
        }),
        ("fit", "القَصّة", "Fit", TailoringOptionInputTypes.Select, false, new[]
        {
            ("regular", "عادية", "Regular", 0m), ("slim", "ضيقة", "Slim", 0m), ("loose", "واسعة", "Loose", 0m),
        }),
        ("fabric_color", "لون القماش", "Fabric Color", TailoringOptionInputTypes.Text, false, Array.Empty<(string, string, string, decimal)>()),
    };

    private static readonly (string Code, string Ar, string En, bool Mandatory, bool Quality)[] Stages =
    {
        ("PREP", "تجهيز الطلب", "Order Preparation", false, false),
        ("FABRIC", "تجهيز القماش", "Fabric Preparation", false, false),
        ("CUT", "القص", "Cutting", true, false),
        ("SEW", "الخياطة", "Sewing", true, false),
        ("EMB", "التطريز", "Embroidery", false, false),
        ("FINISH", "التشطيب والكي", "Finishing & Pressing", true, false),
        ("QC", "فحص الجودة", "Quality Inspection", true, true),
        ("FINAL", "الاعتماد النهائي", "Final Approval", false, false),
    };

    public static void SeedThobe(ErpDbContext db)
    {
        var type = new TailoringGarmentType
        {
            Id = Guid.NewGuid(), Code = ThobeCode, NameAr = "ثوب سعودي رجالي", NameEn = "Saudi Men's Thobe", Category = TailoringGarmentCategories.Men,
            DefaultFabricMeters = 3.5m, MeasurementUnit = TailoringMeasurementUnits.Cm, SortOrder = 1,
        };
        db.Add(type);
        var order = 0;
        foreach (var f in ThobeFields) db.Add(NewField(type.Id, f, ++order, withRange: true));
        order = 0;
        foreach (var o in ThobeOptions) AddOption(db, type.Id, o, ++order);
    }

    /// <summary>
    /// يستكمل قالب الثوب بالحقول والخيارات الافتراضية الناقصة حسب المفتاح؛ الموجود (ولو معطلاً) لا يُكرَّر.
    /// الحقل الذي بقي باسم الإصدار الأول ومجموعته يُنقل لاسم الحالي ومجموعته وترتيبه، والحدود تُضاف لما لا حدود له إن كانت الوحدة سنتيمتراً.
    /// يعيد عدد ما أُضيف.
    /// </summary>
    public static int CompleteThobe(ErpDbContext db, TailoringGarmentType type, List<TailoringMeasurementField> fields, List<TailoringDesignOption> options)
    {
        var cm = type.MeasurementUnit == TailoringMeasurementUnits.Cm;
        var added = 0;
        var order = 0;
        var defaults = ThobeFields.Select(f => f.Key).ToHashSet();
        foreach (var f in ThobeFields)
        {
            order++;
            var e = fields.FirstOrDefault(x => x.Key == f.Key);
            if (e == null) { db.Add(NewField(type.Id, f, order, cm)); added++; continue; }
            if (V1.TryGetValue(f.Key, out var old) && e.GroupName == old.Group)
            {
                e.GroupName = f.Group; e.SortOrder = order;
                if (e.NameAr == old.Ar) { e.NameAr = f.Ar; e.NameEn = f.En; }
            }
            if (cm && e.InputType == TailoringOptionInputTypes.Number && e.MinValue == null && e.MaxValue == null) { e.MinValue = f.Min; e.MaxValue = f.Max; }
        }
        // حقول المستخدم الخاصة تأتي بعد الافتراضية بترتيبها النسبي
        foreach (var e in fields.Where(x => !defaults.Contains(x.Key) && x.SortOrder <= ThobeFields.Length)) e.SortOrder += 100;

        var nextOption = options.Count == 0 ? 0 : options.Max(o => o.SortOrder);
        foreach (var o in ThobeOptions.Where(o => !options.Any(x => x.Key == o.Key)))
        {
            AddOption(db, type.Id, o, ++nextOption);
            added++;
        }
        return added;
    }

    private static TailoringMeasurementField NewField(Guid typeId, Field f, int order, bool withRange) => new()
    {
        GarmentTypeId = typeId, Key = f.Key, NameAr = f.Ar, NameEn = f.En, GroupName = f.Group, IsRequired = f.Required, SortOrder = order,
        MinValue = withRange ? f.Min : null, MaxValue = withRange ? f.Max : null,
        InputType = f.Choices == null ? TailoringOptionInputTypes.Number : TailoringOptionInputTypes.Select,
        ChoicesJson = f.Choices == null ? null
            : TailoringMeasurementJson.WriteChoices(f.Choices.Select(c => new TailoringMeasurementChoiceDto { Code = c.Code, NameAr = c.Ar, NameEn = c.En }).ToList()),
    };

    private static void AddOption(ErpDbContext db, Guid typeId,
        (string Key, string Ar, string En, string Input, bool Required, (string Code, string Ar, string En, decimal Price)[] Values) o, int order)
    {
        var option = new TailoringDesignOption
        {
            Id = Guid.NewGuid(), GarmentTypeId = typeId, Key = o.Key, NameAr = o.Ar, NameEn = o.En, InputType = o.Input, IsRequired = o.Required,
            SortOrder = order,
        };
        db.Add(option);
        var v = 0;
        foreach (var value in o.Values)
            db.Add(new TailoringDesignOptionValue
            {
                OptionId = option.Id, Code = value.Code, NameAr = value.Ar, NameEn = value.En, PriceDelta = value.Price, SortOrder = ++v, IsDefault = v == 1,
            });
    }

    public static void SeedStages(ErpDbContext db)
    {
        var order = 0;
        foreach (var s in Stages)
            db.Add(new TailoringStage { Code = s.Code, NameAr = s.Ar, NameEn = s.En, IsMandatory = s.Mandatory, IsQualityCheck = s.Quality, SortOrder = ++order });
    }
}
