namespace TaskCost;

public sealed class AppSettings
{
    public decimal Ddr3EurPerGb { get; set; } = 1.50m;
    public decimal Ddr4EurPerGb { get; set; } = 2.50m;
    public decimal Ddr5EurPerGb { get; set; } = 4.00m;
    public string RamType { get; set; } = "DDR4";
    public string Currency { get; set; } = "EUR";
    public Dictionary<string, decimal> RatesFromEur { get; set; } = new()
    {
        ["EUR"] = 1m, ["USD"] = 1.16m, ["GBP"] = .87m, ["CHF"] = .93m,
        ["JPY"] = 182m, ["CAD"] = 1.59m, ["AUD"] = 1.78m
    };

    public decimal SelectedEurPrice => RamType switch { "DDR3" => Ddr3EurPerGb, "DDR5" => Ddr5EurPerGb, _ => Ddr4EurPerGb };
    public decimal Rate => RatesFromEur.GetValueOrDefault(Currency, 1m);
    public string Symbol => Currency switch { "EUR" => "€", "USD" => "$", "GBP" => "£", "JPY" => "¥", "CHF" => "CHF ", "CAD" => "C$", "AUD" => "A$", _ => Currency + " " };
}
