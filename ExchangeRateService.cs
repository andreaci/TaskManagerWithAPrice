using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Xml.Linq;

namespace TaskCost;

public sealed class ExchangeRateService
{
    private const string DailyRatesUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<ExchangeRateSnapshot> GetLatestAsync()
    {
        var xml = await Client.GetStringAsync(DailyRatesUrl);
        var document = XDocument.Parse(xml);
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["EUR"] = 1m };

        foreach (var element in document.Descendants().Where(node => node.Name.LocalName == "Cube"))
        {
            var currency = element.Attribute("currency")?.Value;
            var rawRate = element.Attribute("rate")?.Value;
            if (!string.IsNullOrWhiteSpace(currency) && decimal.TryParse(rawRate, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) && rate > 0)
                rates[currency] = rate;
        }

        if (rates.Count == 1) throw new InvalidDataException("The ECB response did not contain exchange rates.");
        var dateText = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "Cube" && node.Attribute("time") is not null)?.Attribute("time")?.Value;
        DateOnly? date = DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) ? parsedDate : null;
        return new ExchangeRateSnapshot(date, rates);
    }
}

public sealed record ExchangeRateSnapshot(DateOnly? Date, IReadOnlyDictionary<string, decimal> RatesFromEur);
