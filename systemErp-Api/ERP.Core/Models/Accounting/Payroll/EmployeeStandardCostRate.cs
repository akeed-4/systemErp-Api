namespace ERP.Core.Models.Accounting;

/// <summary>غرض التكلفة المعيارية للموظف: فني في الورشة أو مشغّل معدة في التأجير.</summary>
public static class EmployeeCostPurposes
{
    public const string Technician = "technician";
    public const string Operator = "operator";

    public static readonly string[] All = { Technician, Operator };
}

/// <summary>
/// التكلفة المعيارية لساعة الموظف بغرضه، سارية من تاريخها حتى السطر التالي (D13: منفصلة عن سعر البيع ومسير الرواتب). سجل يُضاف
/// إليه: تغيّر التكلفة سطر جديد بتاريخ سريانه، والمستندات (ساعات الفنيين، المشغّل في عقد التأجير) تثبّت التكلفة السارية على سطرها
/// فلا يغيّرها سطر لاحق ولا حذفه. <c>Equipment.OperatorPerHour</c> للمقاولات يبقى كما هو.
/// </summary>
public class EmployeeStandardCostRate : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string Purpose { get; set; } = EmployeeCostPurposes.Technician;
    public decimal CostPerHour { get; set; }
    /// <summary>تاريخ السريان (يوم بلا وقت).</summary>
    public DateTime EffectiveFrom { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
