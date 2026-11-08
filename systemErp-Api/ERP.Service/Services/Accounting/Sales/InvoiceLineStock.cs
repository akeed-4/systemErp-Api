using System.Text.Json;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أثر سطر الفاتورة على المخزون بحسب لقطة طبيعة صنفه (<see cref="InvoiceItemBase.ItemType"/>):
/// - بند بلا صنف أو صنف خدمي: لا حركة مخزنية، وتكلفته (إن وُجدت) تكلفة خدمة.
/// - بضاعة أو منتج صناعي: حركة على الصنف نفسه.
/// - مجمّع أو مركّب: حركة على كل مكوّن بكميته (من لقطة المكونات على السطر)، ولا رصيد للصنف نفسه.
/// </summary>
internal static class InvoiceLineStock
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>بند لا يمسّ المخزون: بلا صنف، أو صنف خدمي.</summary>
    public static bool IsNonStock(InvoiceItemBase l)
        => l.ItemId == Guid.Empty || ProductItemTypes.Normalize(l.ItemType) == ProductItemTypes.Service;

    /// <summary>سطر يتحرك صنفه نفسه في المخزون.</summary>
    public static bool MovesItself(InvoiceItemBase l) => l.ItemId != Guid.Empty && ProductItemTypes.TracksStock(l.ItemType);

    /// <summary>سطر صنف مجمّع/مركّب: تتحرك مكوناته.</summary>
    public static bool SellsComponents(InvoiceItemBase l) => l.ItemId != Guid.Empty && ProductItemTypes.SellsComponents(l.ItemType);

    public static List<InvoiceLineComponent> Components(InvoiceItemBase l)
        => string.IsNullOrWhiteSpace(l.ComponentsJson) ? new()
            : JsonSerializer.Deserialize<List<InvoiceLineComponent>>(l.ComponentsJson, Json) ?? new();

    public static void SetComponents(InvoiceItemBase l, List<InvoiceLineComponent> components)
        => l.ComponentsJson = components.Count == 0 ? null : JsonSerializer.Serialize(components, Json);
}
