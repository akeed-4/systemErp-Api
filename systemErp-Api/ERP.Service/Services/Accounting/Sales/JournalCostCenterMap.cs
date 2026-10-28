using System.Text.Json;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مراكز التكلفة التي يختارها المستخدم لحسابات قيد الفاتورة: تُحفظ على الفاتورة (JSON) وتُمرَّر لمحرك الترحيل خريطةً (حساب ← مركز).
/// حساب واحد = مركز واحد (آخر اختيار للحساب هو المعتمد)، والاختيار بلا مركز يعني «مراكز الأسطر كما هي».
/// </summary>
internal static class JournalCostCenterMap
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static List<JournalCostCenterDto> Normalize(IEnumerable<JournalCostCenterDto>? items)
        => (items ?? Enumerable.Empty<JournalCostCenterDto>())
            .Where(i => !string.IsNullOrWhiteSpace(i.AccountCode) && i.CostCenterId != Guid.Empty)
            .GroupBy(i => i.AccountCode.Trim())
            .Select(g => new JournalCostCenterDto { AccountCode = g.Key, CostCenterId = g.Last().CostCenterId })
            .OrderBy(i => i.AccountCode, StringComparer.Ordinal)
            .ToList();

    public static string? Pack(IEnumerable<JournalCostCenterDto>? items)
    {
        var list = Normalize(items);
        return list.Count == 0 ? null : JsonSerializer.Serialize(list, Json);
    }

    public static List<JournalCostCenterDto> Unpack(string? json)
        => string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<List<JournalCostCenterDto>>(json, Json) ?? new();

    public static Dictionary<string, Guid> Map(string? json) => Unpack(json).ToDictionary(i => i.AccountCode, i => i.CostCenterId);
}
