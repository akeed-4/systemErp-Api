using System.Text.Json;

namespace ERP.Service.Services.Accounting;

/// <summary>تحويل قيم الاختيار وصفات الجسم المحفوظة JSON (قراءة متسامحة: النص الفارغ أو التالف = لا شيء).</summary>
internal static class TailoringMeasurementJson
{
    public static string? WriteChoices(List<TailoringMeasurementChoiceDto> choices) => choices.Count == 0 ? null : JsonSerializer.Serialize(choices);

    public static List<TailoringMeasurementChoiceDto> ReadChoices(string? json) => Read<List<TailoringMeasurementChoiceDto>>(json) ?? new();

    public static string? WriteSelected(Dictionary<string, string?> selected) => selected.Count == 0 ? null : JsonSerializer.Serialize(selected);

    public static Dictionary<string, string?> ReadSelected(string? json) => Read<Dictionary<string, string?>>(json) ?? new();

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return null; }
    }
}
