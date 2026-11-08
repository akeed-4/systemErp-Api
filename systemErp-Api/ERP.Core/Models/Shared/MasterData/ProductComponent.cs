namespace ERP.Core.Models.Shared;

/// <summary>
/// طبيعة الصنف: تحدد هل له رصيد، وكيف يُصرف عند البيع، وهل يُصنَّع. الصنف القديم بلا طبيعة = بضاعة.
/// </summary>
public static class ProductItemTypes
{
    /// <summary>صنف بضاعة: له رصيد وحركات مخزنية.</summary>
    public const string Stock = "stock";
    /// <summary>صنف خدمي: لا وجود مخزني له (لا رصيد ولا حركة)، تكلفته المعيارية تكلفة بيعه.</summary>
    public const string Service = "service";
    /// <summary>صنف مجمّع: مكوّنات من أصناف البضاعة، يظهر في فاتورة البيع وحده ويُخصم من رصيد مكوناته. لا وجود له إلا حركة بيع.</summary>
    public const string Kit = "kit";
    /// <summary>صنف مركّب: كالمجمّع، لكن مكوناته تظهر في فاتورة البيع بكمياتها بلا سعر.</summary>
    public const string Composite = "composite";
    /// <summary>منتج صناعي: له رصيد، ويُنتج بمستند تصنيع من مواد أولية وخدمات ومصاريف غير مباشرة.</summary>
    public const string Manufactured = "manufactured";

    public static readonly IReadOnlyList<string> All = new[] { Stock, Service, Kit, Composite, Manufactured };

    public static bool IsValid(string? type) => type is Stock or Service or Kit or Composite or Manufactured;

    /// <summary>الطبيعة المخزّنة (الفارغ = بضاعة).</summary>
    public static string Normalize(string? type) => string.IsNullOrWhiteSpace(type) ? Stock : type.Trim();

    /// <summary>له رصيد وحركات مخزنية بنفسه (بضاعة أو منتج صناعي).</summary>
    public static bool TracksStock(string? type) => Normalize(type) is Stock or Manufactured;

    /// <summary>يُباع بمكوناته: يُخصم من رصيد مكوناته عند البيع ولا رصيد له.</summary>
    public static bool SellsComponents(string? type) => Normalize(type) is Kit or Composite;

    /// <summary>له مكونات معرّفة على الصنف.</summary>
    public static bool HasComponents(string? type) => Normalize(type) is Kit or Composite or Manufactured;
}

/// <summary>نوع سطر مكونات الصنف.</summary>
public static class ProductComponentKinds
{
    /// <summary>صنف آخر بكميته لكل وحدة واحدة (مادة أولية، أو خدمة كشغل اليد، أو مكوّن مجمّع).</summary>
    public const string Item = "item";
    /// <summary>تكلفة غير مباشرة بلا صنف (إهلاك الآلة، رواتب وأجور...) بمبلغ لكل وحدة واحدة؛ للمنتج الصناعي فقط.</summary>
    public const string Overhead = "overhead";

    public static bool IsValid(string? kind) => kind is Item or Overhead;
}

/// <summary>
/// مكوّن صنف: للمجمّع والمركّب أصناف بضاعة بكمياتها لكل وحدة منه؛ وللمنتج الصناعي قائمة مواده (مواد أولية وخدمات) ومصاريفه
/// غير المباشرة لكل وحدة. يقرؤه مستند التصنيع وفاتورة البيع، وكلاهما يحفظ لقطته فلا يغيّر تعديله مستنداً قائماً.
/// </summary>
public class ProductComponent : BaseEntity
{
    /// <summary>الصنف المالك (المجمّع/المركّب/المنتج).</summary>
    public Guid ProductId { get; set; }
    /// <summary><see cref="ProductComponentKinds"/>.</summary>
    public string Kind { get; set; } = ProductComponentKinds.Item;
    /// <summary>الصنف المكوّن في <see cref="ProductComponentKinds.Item"/>.</summary>
    public Guid? ComponentProductId { get; set; }
    /// <summary>الكمية بوحدة المكوّن لكل وحدة واحدة من المالك.</summary>
    public decimal Quantity { get; set; }
    /// <summary>نسبة الهدر في المواد (تزيد الكمية المصروفة في التصنيع).</summary>
    public decimal WastePercent { get; set; }
    /// <summary>اسم المصروف غير المباشر.</summary>
    public string? Name { get; set; }
    /// <summary>مبلغ المصروف غير المباشر لكل وحدة واحدة من المنتج.</summary>
    public decimal UnitCost { get; set; }
    /// <summary>الحساب الدائن الذي يُحمَّل منه المصروف (أو الخدمة) على المنتج؛ الفارغ = تكاليف التصنيع المحمّلة.</summary>
    public string? AccountCode { get; set; }
    public int SortOrder { get; set; }
}
