using System.Net.Http;
using System.Text.Json;

namespace TaskCost;

public sealed class RamTrackService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    public async Task<(decimal? Ddr4, decimal? Ddr5)> TryGetPricesAsync()
    {
        var json = await Client.GetStringAsync("https://ramtrack.eu/api/prices");
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("prices", out var prices)) return (null, null);
        return (AveragePerGb(prices, "DDR4"), AveragePerGb(prices, "DDR5"));
    }

    private static decimal? AveragePerGb(JsonElement prices, string kind)
    {
        if (!prices.TryGetProperty(kind, out var generation)) return null;
        var values = generation.EnumerateObject()
            .Select(capacity => capacity.Value.TryGetProperty("perGb", out var value) && value.TryGetDecimal(out var parsed) ? (decimal?)parsed : null)
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .ToArray();
        return values.Length == 0 ? null : decimal.Round(values.Average(), 2);
    }
}
