using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ERP.Core.Contracts.Shared;
using Microsoft.Extensions.Configuration;

namespace ERP.Service.Services.Shared;

/// <summary>
/// وكيل بحث جغرافي بزر بحث صريح (لا إكمال تلقائي). المزود مجاني من الإعدادات <c>Geocoding:SearchUrl</c> بصيغة نتائج
/// <c>lat/lon/display_name</c> و<c>{q}</c> مكان النص. طلب واحد في الفاصل <c>Geocoding:MinIntervalMs</c> للخادم كله،
/// والنتائج مخزنة مؤقتاً. بلا رابط أو <c>Geocoding:Enabled=false</c> = الإدخال اليدوي وحده.
/// </summary>
public class GeocodingService : IGeocodingService
{
    public const string HttpClientName = "geocoding";
    private const int MaxCacheEntries = 1000;
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);
    private static readonly ConcurrentDictionary<string, (DateTime At, List<GeocodeResultDto> Items)> Cache = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    public GeocodingService(IHttpClientFactory http, IConfiguration config) { _http = http; _config = config; }

    private string? SearchUrl => _config["Geocoding:SearchUrl"];
    public bool Enabled => _config.GetValue("Geocoding:Enabled", false) && !string.IsNullOrWhiteSpace(SearchUrl);

    public async Task<List<GeocodeResultDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (!Enabled) throw new ConflictException(Messages.GeocodingDisabled);
        var q = (query ?? string.Empty).Trim();
        if (q.Length < 3 || q.Length > 200) throw new ValidationFailedException(Messages.GeocodingQueryInvalid);
        var key = q.ToLowerInvariant();
        if (Cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheFor) return hit.Items;

        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, _config.GetValue("Geocoding:MinIntervalMs", 1000)));
        await Gate.WaitAsync(ct);
        try
        {
            var wait = _lastCallUtc + interval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastCallUtc = DateTime.UtcNow;
            var url = SearchUrl!.Replace("{q}", Uri.EscapeDataString(q));
            List<Row>? rows;
            try { rows = await _http.CreateClient(HttpClientName).GetFromJsonAsync<List<Row>>(url, ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                throw new ConflictException(Messages.GeocodingUnavailable);
            }
            var items = (rows ?? new())
                .Select(r => (ok: decimal.TryParse(r.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                                 & decimal.TryParse(r.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng), lat, lng, r.DisplayName))
                .Where(x => x.ok)
                .Select(x => new GeocodeResultDto { DisplayName = x.DisplayName ?? string.Empty, Latitude = Math.Round(x.lat, 6), Longitude = Math.Round(x.lng, 6) })
                .Take(10).ToList();
            if (Cache.Count >= MaxCacheEntries) Cache.Clear();
            Cache[key] = (DateTime.UtcNow, items);
            return items;
        }
        finally { Gate.Release(); }
    }

    private sealed class Row
    {
        [JsonPropertyName("lat")] public string? Lat { get; set; }
        [JsonPropertyName("lon")] public string? Lon { get; set; }
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }
}
