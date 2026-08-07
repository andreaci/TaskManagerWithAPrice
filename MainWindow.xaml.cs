using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace TaskCost;

public partial class MainWindow : Window
{
    private readonly ProcessService _processService = new();
    private readonly RamTrackService _ramTrack = new();
    private readonly ExchangeRateService _exchangeRates = new();
    private readonly MarketDataStore _marketDataStore = new();
    private readonly CancellationTokenSource _pollCancellation = new();
    private readonly SemaphoreSlim _processRefreshLock = new(1, 1);
    private readonly object _settingsLock = new();
    private readonly MarketDataCache _marketData;
    private readonly AppSettings _settings;
    private readonly ObservableCollection<ProcessRow> _details = new();
    private readonly ObservableCollection<ProcessRow> _groups = new();
    private readonly ICollectionView _detailsView;
    private readonly ICollectionView _groupsView;
    private bool _marketRefreshInProgress;

    public MainWindow()
    {
        _marketData = _marketDataStore.Load();
        _settings = _marketData.Settings;
        InitializeComponent();
        RamTypeBox.ItemsSource = new[] { "DDR3", "DDR4", "DDR5" };
        CurrencyBox.ItemsSource = new[] { "EUR", "USD", "GBP", "CHF", "JPY", "CAD", "AUD" };
        _detailsView = CollectionViewSource.GetDefaultView(_details);
        _groupsView = CollectionViewSource.GetDefaultView(_groups);
        _detailsView.Filter = item => item is ProcessRow row && MatchesSearch(row);
        _groupsView.Filter = item => item is ProcessRow row && MatchesSearch(row);
        _detailsView.SortDescriptions.Add(new SortDescription(nameof(ProcessRow.CpuPercent), ListSortDirection.Descending));
        _groupsView.SortDescriptions.Add(new SortDescription(nameof(ProcessRow.CpuPercent), ListSortDirection.Descending));
        DetailsGrid.ItemsSource = _detailsView;
        ProcessesGrid.ItemsSource = _groupsView;
        DetailsGrid.Columns.First(column => column.SortMemberPath == nameof(ProcessRow.CpuPercent)).SortDirection = ListSortDirection.Descending;
        ProcessesGrid.Columns.First(column => column.SortMemberPath == nameof(ProcessRow.CpuPercent)).SortDirection = ListSortDirection.Descending;
        PopulateSettings();
        Loaded += async (_, _) =>
        {
            _ = RefreshMarketDataAsync(force: false);
            RefreshButton.IsEnabled = false;
            ProcessesStatus.Text = "Loading processes\u2026";
            DetailsStatus.Text = "Loading processes\u2026";
            try
            {
                await RefreshProcessesOnceAsync(_pollCancellation.Token);
                _ = Task.Run(() => PollProcessesAsync(_pollCancellation.Token));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ProcessesStatus.Text = $"Initial load failed: {ex.Message}";
                DetailsStatus.Text = $"Initial load failed: {ex.Message}";
            }
            finally { RefreshButton.IsEnabled = true; }
        };
        Closed += (_, _) => _pollCancellation.Cancel();
    }

    private async Task PollProcessesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                await RefreshProcessesOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => DetailsStatus.Text = $"Update failed: {ex.Message}", DispatcherPriority.Background);
            }

        }
    }

    private async Task RefreshProcessesOnceAsync(CancellationToken cancellationToken)
    {
        await _processRefreshLock.WaitAsync(cancellationToken);
        try
        {
            var details = await Task.Run(_processService.Capture, cancellationToken);
            var groups = ProcessService.Group(details);
            PriceSnapshot price;
            lock (_settingsLock) price = GetPriceSnapshot();
            CalculateCosts(details, price);
            CalculateCosts(groups, price);
            await Dispatcher.InvokeAsync(() => PublishSnapshot(details, groups, price), DispatcherPriority.Background, cancellationToken);
        }
        finally { _processRefreshLock.Release(); }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        ProcessesStatus.Text = "Refreshing\u2026";
        DetailsStatus.Text = "Refreshing\u2026";
        try { await RefreshProcessesOnceAsync(_pollCancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ProcessesStatus.Text = $"Refresh failed: {ex.Message}";
            DetailsStatus.Text = $"Refresh failed: {ex.Message}";
        }
        finally { RefreshButton.IsEnabled = true; }
    }

    private void PublishSnapshot(List<ProcessRow> details, List<ProcessRow> groups, PriceSnapshot price)
    {
        MergeRows(_details, details, row => row.Id.ToString(CultureInfo.InvariantCulture));
        MergeRows(_groups, groups, row => row.Name);
        HeaderPrice.Text = $"{price.RamType}  {price.Symbol}{price.PricePerGb:0.00} / GB";
        UpdateStatus();
        UpdateSummary();
    }

    private static void MergeRows(
        ObservableCollection<ProcessRow> target,
        IReadOnlyCollection<ProcessRow> snapshot,
        Func<ProcessRow, string> keySelector)
    {
        var current = target.ToDictionary(keySelector, StringComparer.OrdinalIgnoreCase);
        var incomingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var incoming in snapshot)
        {
            var key = keySelector(incoming);
            incomingKeys.Add(key);
            if (current.TryGetValue(key, out var existing)) existing.UpdateFrom(incoming);
            else target.Add(incoming);
        }

        for (var index = target.Count - 1; index >= 0; index--)
            if (!incomingKeys.Contains(keySelector(target[index]))) target.RemoveAt(index);
    }

    private static void CalculateCosts(IEnumerable<ProcessRow> rows, PriceSnapshot price)
    {
        foreach (var row in rows)
        {
            row.CurrencySymbol = price.Symbol;
            row.Cost = (decimal)row.WorkingSetBytes / 1_073_741_824m * price.PricePerGb;
            row.RefreshCalculated();
        }
    }

    private bool MatchesSearch(ProcessRow row)
    {
        var query = SearchBox.Text.Trim();
        return query.Length == 0 ||
            row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFiltersAndStatus()
    {
        _detailsView.Refresh();
        _groupsView.Refresh();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        DetailsStatus.Text = $"{_detailsView.Cast<object>().Count():N0} of {_details.Count:N0} processes \u00B7 updated {DateTime.Now:T}";
        ProcessesStatus.Text = $"{_groupsView.Cast<object>().Count():N0} groups \u00B7 updated {DateTime.Now:T}";
    }

    private void UpdateSummary()
    {
        CpuSummary.Text = $"{Math.Min(100, _details.Where(row => row.Id != 0).Sum(row => row.CpuPercent)):0}%";
        MemorySummary.Text = TryMemoryLoad(out var load) ? $"{load}%" : "\u2014";
        CostSummary.Text = $"{GetPriceSnapshot().Symbol}{_details.Sum(row => row.Cost):0.00}";
        ProcessSummary.Text = _details.Count.ToString(CultureInfo.CurrentCulture);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DetailsGrid is not null && _detailsView is not null) RefreshFiltersAndStatus();
    }

    private void Processes_Click(object sender, RoutedEventArgs e) => ShowView(ProcessesView);
    private void Details_Click(object sender, RoutedEventArgs e) => ShowView(DetailsView);
    private void Settings_Click(object sender, RoutedEventArgs e) { PopulateSettings(); ShowView(SettingsView); }
    private void ShowView(UIElement view)
    {
        ProcessesView.Visibility = view == ProcessesView ? Visibility.Visible : Visibility.Collapsed;
        DetailsView.Visibility = view == DetailsView ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = view == SettingsView ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateSettings()
    {
        Ddr3Box.Text = _settings.Ddr3EurPerGb.ToString("0.00", CultureInfo.CurrentCulture);
        Ddr4Box.Text = _settings.Ddr4EurPerGb.ToString("0.00", CultureInfo.CurrentCulture);
        Ddr5Box.Text = _settings.Ddr5EurPerGb.ToString("0.00", CultureInfo.CurrentCulture);
        RamTypeBox.SelectedItem = _settings.RamType;
        CurrencyBox.SelectedItem = _settings.Currency;
        RateBox.Text = _settings.Rate.ToString("0.########", CultureInfo.CurrentCulture);
        RamTrackStatus.Text = _marketData.RamPricesCustomized
            ? "Custom RAM prices; automatic updates disabled."
            : FormatCheckedStatus("RAMTrack", _marketData.RamPricesCheckedUtc);
        ExchangeRateStatus.Text = _marketData.ExchangeRatesCustomized
            ? "Custom exchange rate; automatic updates disabled."
            : FormatCheckedStatus("ECB", _marketData.ExchangeRatesCheckedUtc);
        UpdateFormulaPreview();
    }

    private void CurrencyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurrencyBox.SelectedItem is string currency && RateBox is not null)
            RateBox.Text = _settings.RatesFromEur.GetValueOrDefault(currency, 1m).ToString("0.########", CultureInfo.CurrentCulture);
        UpdateFormulaPreview();
    }
    private void SettingsSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFormulaPreview();
    private void UpdateFormulaPreview()
    {
        if (FormulaPreview is null) return;
        var type = RamTypeBox.SelectedItem?.ToString() ?? _settings.RamType;
        var currency = CurrencyBox.SelectedItem?.ToString() ?? _settings.Currency;
        var price = type switch { "DDR3" => ParseDecimal(Ddr3Box?.Text, _settings.Ddr3EurPerGb), "DDR5" => ParseDecimal(Ddr5Box?.Text, _settings.Ddr5EurPerGb), _ => ParseDecimal(Ddr4Box?.Text, _settings.Ddr4EurPerGb) };
        var rate = ParseDecimal(RateBox?.Text, _settings.RatesFromEur.GetValueOrDefault(currency, 1m));
        FormulaPreview.Text = $"Memory value = working set (GB) \u00D7 {price:0.00} EUR/GB \u00D7 {rate:0.####}. Example: a 500 MB process is worth approximately {CurrencySymbol(currency)}{(.5m * price * rate):0.0000}.";
    }

    private void ApplySettings_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePositive(Ddr3Box.Text, out var ddr3) || !TryParsePositive(Ddr4Box.Text, out var ddr4) || !TryParsePositive(Ddr5Box.Text, out var ddr5) || !TryParsePositive(RateBox.Text, out var rate))
        {
            SaveStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            SaveStatus.Text = "Enter positive numeric values.";
            return;
        }
        lock (_settingsLock)
        {
            var selectedCurrency = CurrencyBox.SelectedItem?.ToString() ?? "EUR";
            var existingRate = _settings.RatesFromEur.GetValueOrDefault(selectedCurrency, 1m);
            if (ddr4 != _settings.Ddr4EurPerGb || ddr5 != _settings.Ddr5EurPerGb) _marketData.RamPricesCustomized = true;
            if (decimal.Round(rate, 8) != decimal.Round(existingRate, 8)) _marketData.ExchangeRatesCustomized = true;
            _settings.Ddr3EurPerGb = ddr3; _settings.Ddr4EurPerGb = ddr4; _settings.Ddr5EurPerGb = ddr5;
            _settings.RamType = RamTypeBox.SelectedItem?.ToString() ?? "DDR4";
            _settings.Currency = selectedCurrency;
            _settings.RatesFromEur[_settings.Currency] = rate;
        }
        try
        {
            _marketDataStore.Save(_marketData);
            RecalculateCurrentRows();
            PopulateSettings();
            SaveStatus.Foreground = System.Windows.Media.Brushes.ForestGreen;
            SaveStatus.Text = "Saved.";
        }
        catch (Exception ex)
        {
            SaveStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            SaveStatus.Text = $"Could not save: {ex.Message}";
        }
    }

    private async void DownloadMarketData_Click(object sender, RoutedEventArgs e)
    {
        _marketData.RamPricesCustomized = false;
        _marketData.ExchangeRatesCustomized = false;
        _marketData.RamPricesCheckedUtc = null;
        _marketData.ExchangeRatesCheckedUtc = null;
        try
        {
            _marketDataStore.Save(_marketData);
            await RefreshMarketDataAsync(force: true);
        }
        catch (Exception ex) { MarketDataStatus.Text = $"Could not reset cache: {ex.Message}"; }
    }

    private async Task RefreshMarketDataAsync(bool force)
    {
        if (_marketRefreshInProgress) return;
        var now = DateTimeOffset.UtcNow;
        var refreshRam = force || (!_marketData.RamPricesCustomized && !WasCheckedToday(_marketData.RamPricesCheckedUtc, now));
        var refreshRates = force || (!_marketData.ExchangeRatesCustomized && !WasCheckedToday(_marketData.ExchangeRatesCheckedUtc, now));
        if (!refreshRam && !refreshRates)
        {
            MarketDataStatus.Text = _marketData.RamPricesCustomized || _marketData.ExchangeRatesCustomized
                ? "Using saved custom values."
                : "Daily market data is already cached.";
            return;
        }

        _marketRefreshInProgress = true;
        DownloadMarketDataButton.IsEnabled = false;
        MarketDataStatus.Text = "Downloading daily market data\u2026";
        if (refreshRam) { _marketData.RamPricesCheckedUtc = now; RamTrackStatus.Text = "Checking RAMTrack\u2026"; }
        if (refreshRates) { _marketData.ExchangeRatesCheckedUtc = now; ExchangeRateStatus.Text = "Checking ECB\u2026"; }

        try
        {
            // Persist the attempt first so repeated launches never contact a service more than once that day.
            _marketDataStore.Save(_marketData);
            var ramTask = refreshRam ? _ramTrack.TryGetPricesAsync() : null;
            var ratesTask = refreshRates ? _exchangeRates.GetLatestAsync() : null;
            var successCount = 0;

            if (ramTask is not null)
            {
                try
                {
                    var prices = await ramTask;
                    lock (_settingsLock)
                    {
                        if (prices.Ddr4 is { } ddr4) _settings.Ddr4EurPerGb = ddr4;
                        if (prices.Ddr5 is { } ddr5) _settings.Ddr5EurPerGb = ddr5;
                    }
                    RamTrackStatus.Text = prices.Ddr4 is null && prices.Ddr5 is null ? "RAMTrack returned no prices." : "RAMTrack prices cached.";
                    if (prices.Ddr4 is not null || prices.Ddr5 is not null) successCount++;
                }
                catch { RamTrackStatus.Text = "RAMTrack unavailable; using cached prices."; }
            }

            if (ratesTask is not null)
            {
                try
                {
                    var rates = await ratesTask;
                    lock (_settingsLock)
                    {
                        foreach (var currency in CurrencyBox.Items.Cast<string>())
                            if (rates.RatesFromEur.TryGetValue(currency, out var rate)) _settings.RatesFromEur[currency] = rate;
                    }
                    ExchangeRateStatus.Text = rates.Date is { } date ? $"ECB rates cached from {date:yyyy-MM-dd}." : "ECB rates cached.";
                    successCount++;
                }
                catch { ExchangeRateStatus.Text = "ECB unavailable; using cached rates."; }
            }

            _marketDataStore.Save(_marketData);
            PopulateSettings();
            RecalculateCurrentRows();
            MarketDataStatus.Text = successCount > 0 ? "Daily market data saved." : "Services unavailable; cached values retained.";
        }
        catch (Exception ex)
        {
            MarketDataStatus.Text = $"Could not update cache: {ex.Message}";
        }
        finally
        {
            _marketRefreshInProgress = false;
            DownloadMarketDataButton.IsEnabled = true;
        }
    }

    private void RecalculateCurrentRows()
    {
        PriceSnapshot price;
        lock (_settingsLock) price = GetPriceSnapshot();
        CalculateCosts(_details, price); CalculateCosts(_groups, price);
        HeaderPrice.Text = $"{price.RamType}  {price.Symbol}{price.PricePerGb:0.00} / GB";
        UpdateStatus(); UpdateSummary(); UpdateFormulaPreview();
    }

    private static bool WasCheckedToday(DateTimeOffset? checkedUtc, DateTimeOffset nowUtc) => checkedUtc?.UtcDateTime.Date == nowUtc.UtcDateTime.Date;
    private static string FormatCheckedStatus(string source, DateTimeOffset? checkedUtc) => checkedUtc is { } value
        ? $"{source} checked {value.ToLocalTime():g}."
        : $"{source} has not been checked yet.";

    private void OpenRamTrack_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://ramtrack.eu/") { UseShellExecute = true });
    private PriceSnapshot GetPriceSnapshot() => new(_settings.RamType, _settings.Symbol, _settings.SelectedEurPrice * _settings.Rate);
    private static decimal ParseDecimal(string? text, decimal fallback) => TryParsePositive(text, out var result) ? result : fallback;
    private static bool TryParsePositive(string? text, out decimal result) => decimal.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out result) && result > 0;
    private static string CurrencySymbol(string currency) => currency switch { "EUR" => "\u20AC", "USD" => "$", "GBP" => "\u00A3", "JPY" => "\u00A5", "CHF" => "CHF ", "CAD" => "C$", "AUD" => "A$", _ => currency + " " };

    private readonly record struct PriceSnapshot(string RamType, string Symbol, decimal PricePerGb);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus { public uint Length; public uint MemoryLoad; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private static bool TryMemoryLoad(out uint load)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var ok = GlobalMemoryStatusEx(ref status); load = status.MemoryLoad; return ok;
    }
}
