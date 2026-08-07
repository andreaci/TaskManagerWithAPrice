using System.IO;
using System.Text.Json;

namespace TaskCost;

public sealed class MarketDataCache
{
    public AppSettings Settings { get; set; } = new();
    public DateTimeOffset? RamPricesCheckedUtc { get; set; }
    public DateTimeOffset? ExchangeRatesCheckedUtc { get; set; }
    public bool RamPricesCustomized { get; set; }
    public bool ExchangeRatesCustomized { get; set; }
}

public sealed class MarketDataStore
{
    private readonly string _path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskCost",
        "market-data.json");

    public string CachePath => _path;

    public MarketDataCache Load()
    {
        try
        {
            var cache = JsonSerializer.Deserialize<MarketDataCache>(File.ReadAllText(_path));
            if (cache?.Settings?.RatesFromEur is { Count: > 0 }) return cache;
        }
        catch { }
        return new MarketDataCache();
    }

    public void Save(MarketDataCache cache)
    {
        var directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, _path, true);
    }
}
