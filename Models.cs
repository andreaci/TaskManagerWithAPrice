using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskCost;

public sealed class ProcessRow : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Status { get; init; } = "Running";
    public double CpuPercent { get; set; }
    public long WorkingSetBytes { get; init; }
    public long PrivateBytes { get; init; }
    public long VirtualBytes { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public string Priority { get; init; } = "—";
    public string Architecture { get; init; } = "—";
    public string Description { get; init; } = "";
    public string FilePath { get; init; } = "";
    public DateTime? Started { get; init; }
    public TimeSpan? CpuTime { get; init; }
    public int InstanceCount { get; init; } = 1;
    public decimal Cost { get; set; }
    public string CurrencySymbol { get; set; } = "€";

    public string MemoryText => FormatBytes(WorkingSetBytes);
    public string PrivateText => FormatBytes(PrivateBytes);
    public string VirtualText => FormatBytes(VirtualBytes);
    public string CpuText => $"{CpuPercent:0.0}%";
    public string CostText => $"{CurrencySymbol}{Cost:0.0000}";
    public string StartedText => Started?.ToString("g") ?? "—";
    public string CpuTimeText => CpuTime?.ToString(@"hh\:mm\:ss") ?? "—";
    public string NameWithCount => InstanceCount > 1 ? $"{DisplayName} ({InstanceCount})" : DisplayName;

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshCalculated() { OnPropertyChanged(nameof(CpuText)); OnPropertyChanged(nameof(CostText)); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private static string FormatBytes(long value) => value switch
    {
        >= 1_073_741_824 => $"{value / 1_073_741_824d:0.0} GB",
        >= 1_048_576 => $"{value / 1_048_576d:0.0} MB",
        _ => $"{value / 1024d:0} KB"
    };
}
