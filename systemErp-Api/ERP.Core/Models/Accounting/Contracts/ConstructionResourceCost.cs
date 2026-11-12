namespace ERP.Core.Models.Accounting;

/// <summary>
/// سطر في سجل تكلفة مورد مكتبة المقاولات: التكلفة بوحدة المورد سارية من تاريخ، بمصدرها وتفصيلها ومن أدخلها. السجل يُضاف إليه فقط:
/// لا يُعدَّل سطر ولا يُحذف (إلا مع حذف مورده غير المستخدم)، ويحرس ذلك <c>ErpDbContext</c> تحت كل مسارات الحفظ.
/// التكلفة السارية في يوم = أحدث سطر تاريخه ≤ اليوم (والأحدث إدخالاً عند تساوي التاريخ).
/// </summary>
public class ConstructionResourceCost : BaseEntity
{
    public Guid ResourceId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    /// <summary>التكلفة بوحدة المورد (<see cref="Unit"/>).</summary>
    public decimal UnitCost { get; set; }
    /// <summary>عملة المنشأة وقت الإدخال (عملة واحدة في هذه المرحلة).</summary>
    public string Currency { get; set; } = string.Empty;
    /// <summary><see cref="ResourceCostSources"/> وقت الإدخال.</summary>
    public string Source { get; set; } = ResourceCostSources.Manual;
    /// <summary>وحدة المورد وقت الإدخال.</summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>ساعات العمل في وحدة المورد وقت الإدخال.</summary>
    public decimal HoursPerUnit { get; set; } = 1m;

    // ---- تفصيل التكلفة (مصدر breakdown) ----
    /// <summary><see cref="ProjectRateBases"/>؛ فارغ لغير التفصيل.</summary>
    public string? RateBasis { get; set; }
    public decimal Rate { get; set; }
    public decimal HoursPerDay { get; set; }
    public decimal DaysPerMonth { get; set; }
    public decimal FuelPerHour { get; set; }
    public decimal MaintenancePerHour { get; set; }
    /// <summary>الإهلاك بالساعة (يُدخل يدوياً في هذه المرحلة).</summary>
    public decimal DepreciationPerHour { get; set; }
    public decimal OperatorPerHour { get; set; }
    public decimal OtherPerHour { get; set; }
    public decimal OvertimeMultiplier { get; set; }
    /// <summary>تكلفة الساعة الناتجة من التفصيل.</summary>
    public decimal HourlyCost { get; set; }
    public decimal OvertimeHourlyCost { get; set; }

    /// <summary>سبب الإدخال (<see cref="ResourceCostReasons"/> أو نص المستخدم).</summary>
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>أسباب أسطر السجل التي يكتبها النظام (رموز تترجمها الواجهة)؛ سبب المستخدم نص حر.</summary>
public static class ResourceCostReasons
{
    /// <summary>سطر افتتاحي من التكلفة القائمة عند تفعيل السجل.</summary>
    public const string Migration = "migration";
    public const string Created = "created";
    public const string Updated = "updated";
    /// <summary>تحديث التكاليف من مصادرها (الصنف وشؤون الموظفين).</summary>
    public const string Refresh = "refresh";
    /// <summary>ربط المادة بصنف مخزني.</summary>
    public const string ProductLink = "product_link";
    /// <summary>إعادة حساب مورد مركّب بعد تغيّر مكوناته.</summary>
    public const string Composite = "composite";
    /// <summary>تعديل التكلفة من سطر الاحتياج في محرر البنود.</summary>
    public const string WorkItem = "work_item";
    /// <summary>تغيّر حفظه مسار لم يسجّل سطره بنفسه (يضيفه حارس الحفظ).</summary>
    public const string System = "system";
}
