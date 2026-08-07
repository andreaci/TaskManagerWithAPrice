using System.ComponentModel;

namespace TaskCost;

public sealed class ProcessRow : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "Running";
    public double CpuPercent { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public long VirtualBytes { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public string Priority { get; set; } = "\u2014";
    public string Architecture { get; set; } = "\u2014";
    public string Description { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime? Started { get; set; }
    public TimeSpan? CpuTime { get; set; }
    public int InstanceCount { get; set; } = 1;
    public decimal Cost { get; set; }
    public string CurrencySymbol { get; set; } = "\u20AC";
    public double? HeatLevel { get; private set; }
    public string? HeatSortMember { get; private set; }

    public string MemoryText => FormatBytes(WorkingSetBytes);
    public string PrivateText => FormatBytes(PrivateBytes);
    public string VirtualText => FormatBytes(VirtualBytes);
    public string CpuText => $"{CpuPercent:0.0}%";
    public string CostText => $"{CurrencySymbol}{Cost:0.0000}";
    public string StartedText => Started?.ToString("g") ?? "\u2014";
    public string CpuTimeText => CpuTime?.ToString(@"hh\:mm\:ss") ?? "\u2014";
    public string NameWithCount => InstanceCount > 1 ? $"{DisplayName} ({InstanceCount})" : DisplayName;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateFrom(ProcessRow source)
    {
        if (Id != source.Id) { Id = source.Id; Notify(nameof(Id)); }
        if (Name != source.Name) { Name = source.Name; Notify(nameof(Name)); }
        if (DisplayName != source.DisplayName) { DisplayName = source.DisplayName; Notify(nameof(DisplayName), nameof(NameWithCount)); }
        if (Status != source.Status) { Status = source.Status; Notify(nameof(Status)); }
        if (CpuPercent != source.CpuPercent) { CpuPercent = source.CpuPercent; Notify(nameof(CpuPercent), nameof(CpuText)); }
        if (WorkingSetBytes != source.WorkingSetBytes) { WorkingSetBytes = source.WorkingSetBytes; Notify(nameof(WorkingSetBytes), nameof(MemoryText)); }
        if (PrivateBytes != source.PrivateBytes) { PrivateBytes = source.PrivateBytes; Notify(nameof(PrivateBytes), nameof(PrivateText)); }
        if (VirtualBytes != source.VirtualBytes) { VirtualBytes = source.VirtualBytes; Notify(nameof(VirtualBytes), nameof(VirtualText)); }
        if (ThreadCount != source.ThreadCount) { ThreadCount = source.ThreadCount; Notify(nameof(ThreadCount)); }
        if (HandleCount != source.HandleCount) { HandleCount = source.HandleCount; Notify(nameof(HandleCount)); }
        if (Priority != source.Priority) { Priority = source.Priority; Notify(nameof(Priority)); }
        if (Architecture != source.Architecture) { Architecture = source.Architecture; Notify(nameof(Architecture)); }
        if (Description != source.Description) { Description = source.Description; Notify(nameof(Description)); }
        if (FilePath != source.FilePath) { FilePath = source.FilePath; Notify(nameof(FilePath)); }
        if (Started != source.Started) { Started = source.Started; Notify(nameof(Started), nameof(StartedText)); }
        if (CpuTime != source.CpuTime) { CpuTime = source.CpuTime; Notify(nameof(CpuTime), nameof(CpuTimeText)); }
        if (InstanceCount != source.InstanceCount) { InstanceCount = source.InstanceCount; Notify(nameof(InstanceCount), nameof(NameWithCount)); }
        if (Cost != source.Cost || CurrencySymbol != source.CurrencySymbol)
        {
            Cost = source.Cost; CurrencySymbol = source.CurrencySymbol;
            Notify(nameof(Cost), nameof(CurrencySymbol), nameof(CostText));
        }
    }

    public void RefreshCalculated() => Notify(nameof(CpuText), nameof(CostText));

    public void SetHeat(string? sortMember, double? level)
    {
        if (HeatSortMember == sortMember && HeatLevel == level) return;
        HeatSortMember = sortMember;
        HeatLevel = level;
        Notify(nameof(HeatSortMember), nameof(HeatLevel));
    }

    private void Notify(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static string FormatBytes(long value) => value switch
    {
        >= 1_073_741_824 => $"{value / 1_073_741_824d:0.0} GB",
        >= 1_048_576 => $"{value / 1_048_576d:0.0} MB",
        _ => $"{value / 1024d:0} KB"
    };
}
