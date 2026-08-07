using System.ComponentModel;
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
    private readonly SettingsStore _settingsStore = new();
    private readonly RamTrackService _ramTrack = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AppSettings _settings;
    private List<ProcessRow> _details = new();
    private List<ProcessRow> _groups = new();

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        RamTypeBox.ItemsSource = new[] { "DDR3", "DDR4", "DDR5" };
        CurrencyBox.ItemsSource = new[] { "EUR", "USD", "GBP", "CHF", "JPY", "CAD", "AUD" };
        PopulateSettings();
        _timer.Tick += async (_, _) => await RefreshProcessesAsync();
        Loaded += async (_, _) =>
        {
            await RefreshProcessesAsync();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private async Task RefreshProcessesAsync()
    {
        if (!await _refreshLock.WaitAsync(0)) return;
        try
        {
            var captured = await Task.Run(_processService.Capture);
            ApplyCosts(captured);
            _details = captured.OrderByDescending(x => x.CpuPercent).ThenBy(x => x.Name).ToList();
            _groups = ProcessService.Group(captured);
            ApplyCosts(_groups);
            ApplyFilter();
            UpdateSummary();
        }
        finally { _refreshLock.Release(); }
    }

    private void ApplyCosts(IEnumerable<ProcessRow> rows)
    {
        foreach (var row in rows)
        {
            row.CurrencySymbol = _settings.Symbol;
            row.Cost = (decimal)row.WorkingSetBytes / 1_073_741_824m * _settings.SelectedEurPrice * _settings.Rate;
            row.RefreshCalculated();
        }
        HeaderPrice.Text = $"{_settings.RamType}  {_settings.Symbol}{_settings.SelectedEurPrice * _settings.Rate:0.00} / GB";
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        bool Match(ProcessRow row) => query.Length == 0 || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Description.Contains(query, StringComparison.OrdinalIgnoreCase) || row.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
        var details = _details.Where(Match).ToList();
        var groups = _groups.Where(Match).ToList();
        DetailsGrid.ItemsSource = details;
        ProcessesGrid.ItemsSource = groups;
        DetailsStatus.Text = $"{details.Count:N0} of {_details.Count:N0} processes · updated {DateTime.Now:T}";
        ProcessesStatus.Text = $"{groups.Count:N0} groups · updated {DateTime.Now:T}";
    }

    private void UpdateSummary()
    {
        CpuSummary.Text = $"{Math.Min(100, _details.Where(x => x.Id != 0).Sum(x => x.CpuPercent)):0}%";
        MemorySummary.Text = TryMemoryLoad(out var load) ? $"{load}%" : "—";
        CostSummary.Text = $"{_settings.Symbol}{_details.Sum(x => x.Cost):0.00}";
        ProcessSummary.Text = _details.Count.ToString(CultureInfo.CurrentCulture);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DetailsGrid is not null) ApplyFilter();
    }

    private void Processes_Click(object sender, RoutedEventArgs e) => ShowView(ProcessesView);
    private void Details_Click(object sender, RoutedEventArgs e) => ShowView(DetailsView);
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        PopulateSettings();
        ShowView(SettingsView);
    }
    private void ShowView(UIElement view)
    {
        ProcessesView.Visibility = view == ProcessesView ? Visibility.Visible : Visibility.Collapsed;
        DetailsView.Visibility = view == DetailsView ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = view == SettingsView ? Visibility.Visible : Visibility.Collapsed;
        EndTaskButton.Visibility = view == SettingsView ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PopulateSettings()
    {
        Ddr3Box.Text = _settings.Ddr3EurPerGb.ToString("0.00", CultureInfo.CurrentCulture);
        Ddr4Box.Text = _settings.Ddr4EurPerGb.ToString("0.00", CultureInfo.CurrentCulture);
        Ddr5Box.Text = _settings.Ddr5EurPerGb.ToString("0.00", CultureInfo.CurrentCulture);
        RamTypeBox.SelectedItem = _settings.RamType;
        CurrencyBox.SelectedItem = _settings.Currency;
        RateBox.Text = _settings.Rate.ToString("0.####", CultureInfo.CurrentCulture);
        UpdateFormulaPreview();
    }

    private void CurrencyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurrencyBox.SelectedItem is string currency && RateBox is not null)
            RateBox.Text = _settings.RatesFromEur.GetValueOrDefault(currency, 1m).ToString("0.####", CultureInfo.CurrentCulture);
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
        FormulaPreview.Text = $"Memory value = working set (GB) × {price:0.00} EUR/GB × {rate:0.####}.  Example: a 500 MB process is worth approximately {CurrencySymbol(currency)}{(.5m * price * rate):0.0000}.";
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePositive(Ddr3Box.Text, out var ddr3) || !TryParsePositive(Ddr4Box.Text, out var ddr4) || !TryParsePositive(Ddr5Box.Text, out var ddr5) || !TryParsePositive(RateBox.Text, out var rate))
        {
            SaveStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            SaveStatus.Text = "Enter positive numeric values.";
            return;
        }
        _settings.Ddr3EurPerGb = ddr3; _settings.Ddr4EurPerGb = ddr4; _settings.Ddr5EurPerGb = ddr5;
        _settings.RamType = RamTypeBox.SelectedItem?.ToString() ?? "DDR4";
        _settings.Currency = CurrencyBox.SelectedItem?.ToString() ?? "EUR";
        _settings.RatesFromEur[_settings.Currency] = rate;
        _settingsStore.Save(_settings);
        ApplyCosts(_details); ApplyCosts(_groups); ApplyFilter(); UpdateSummary(); UpdateFormulaPreview();
        SaveStatus.Foreground = System.Windows.Media.Brushes.ForestGreen;
        SaveStatus.Text = "Saved.";
    }

    private async void RamTrack_Click(object sender, RoutedEventArgs e)
    {
        RamTrackStatus.Text = "Checking…";
        try
        {
            var result = await _ramTrack.TryGetPricesAsync();
            if (result.Ddr4 is null && result.Ddr5 is null)
                RamTrackStatus.Text = "Live values are client-rendered; enter them manually.";
            else
            {
                if (result.Ddr4 is { } d4) Ddr4Box.Text = d4.ToString("0.00", CultureInfo.CurrentCulture);
                if (result.Ddr5 is { } d5) Ddr5Box.Text = d5.ToString("0.00", CultureInfo.CurrentCulture);
                RamTrackStatus.Text = "Updated. Review, then save.";
                UpdateFormulaPreview();
            }
        }
        catch { RamTrackStatus.Text = "Could not read RAMTrack. Custom values still work."; }
    }

    private void OpenRamTrack_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://ramtrack.eu/") { UseShellExecute = true });

    private void EndTask_Click(object sender, RoutedEventArgs e)
    {
        var selected = DetailsView.Visibility == Visibility.Visible ? DetailsGrid.SelectedItem as ProcessRow : ProcessesGrid.SelectedItem as ProcessRow;
        if (selected is null) { MessageBox.Show(this, "Select a process first.", "TaskCost", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (selected.Id == Environment.ProcessId) { MessageBox.Show(this, "TaskCost cannot end itself from this view.", "TaskCost", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show(this, $"End {selected.Name} (PID {selected.Id})?\n\nUnsaved data in that process may be lost.", "End task", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { using var process = Process.GetProcessById(selected.Id); process.Kill(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not end task", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e) { }
    private static decimal ParseDecimal(string? text, decimal fallback) => TryParsePositive(text, out var result) ? result : fallback;
    private static bool TryParsePositive(string? text, out decimal result)
    {
        var normalized = (text ?? "").Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result) && result > 0;
    }
    private static string CurrencySymbol(string currency) => currency switch { "EUR" => "€", "USD" => "$", "GBP" => "£", "JPY" => "¥", "CHF" => "CHF ", "CAD" => "C$", "AUD" => "A$", _ => currency + " " };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus { public uint Length; public uint MemoryLoad; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private static bool TryMemoryLoad(out uint load)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var ok = GlobalMemoryStatusEx(ref status); load = status.MemoryLoad; return ok;
    }
}
