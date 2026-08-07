using System.Diagnostics;

namespace TaskCost;

public sealed class ProcessService
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _previous = new();
    public List<ProcessRow> Capture()
    {
        var now = DateTime.UtcNow;
        var logical = Math.Max(1, Environment.ProcessorCount);
        var rows = new List<ProcessRow>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var cpu = process.TotalProcessorTime;
                var percent = 0d;
                if (_previous.TryGetValue(process.Id, out var old))
                    percent = Math.Clamp((cpu - old.Cpu).TotalMilliseconds / (now - old.At).TotalMilliseconds / logical * 100, 0, 100);
                _previous[process.Id] = (cpu, now);
                string path = "", description = process.ProcessName, priority = "—";
                DateTime? started = null;
                try { path = process.MainModule?.FileName ?? ""; description = process.MainModule?.FileVersionInfo.FileDescription ?? process.ProcessName; } catch { }
                try { priority = process.PriorityClass.ToString(); } catch { }
                try { started = process.StartTime; } catch { }
                rows.Add(new ProcessRow
                {
                    Id = process.Id, Name = process.ProcessName, DisplayName = string.IsNullOrWhiteSpace(description) ? process.ProcessName : description,
                    CpuPercent = percent, WorkingSetBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64,
                    VirtualBytes = process.VirtualMemorySize64, ThreadCount = process.Threads.Count, HandleCount = process.HandleCount,
                    Priority = priority, Architecture = GetArchitecture(process), Description = description, FilePath = path, Started = started, CpuTime = cpu
                });
            }
            catch { }
            finally { process.Dispose(); }
        }
        foreach (var stale in _previous.Keys.Except(rows.Select(x => x.Id)).ToArray()) _previous.Remove(stale);
        return rows;
    }

    public static List<ProcessRow> Group(IEnumerable<ProcessRow> rows) => rows.Where(x => x.Id != 0).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g =>
    {
        var first = g.First();
        return new ProcessRow
        {
            Id = first.Id, Name = first.Name, DisplayName = first.DisplayName, InstanceCount = g.Count(),
            CpuPercent = g.Sum(x => x.CpuPercent), WorkingSetBytes = g.Sum(x => x.WorkingSetBytes), PrivateBytes = g.Sum(x => x.PrivateBytes),
            VirtualBytes = g.Sum(x => x.VirtualBytes), ThreadCount = g.Sum(x => x.ThreadCount), HandleCount = g.Sum(x => x.HandleCount),
            Priority = first.Priority, Architecture = first.Architecture, Description = first.Description, FilePath = first.FilePath,
            Started = g.Min(x => x.Started), CpuTime = TimeSpan.FromTicks(g.Sum(x => x.CpuTime?.Ticks ?? 0))
        };
    }).OrderByDescending(x => x.CpuPercent).ToList();

    private static string GetArchitecture(Process process)
    {
        if (!Environment.Is64BitOperatingSystem) return "x86";
        try { return process.MainModule?.FileName.Contains("SysWOW64", StringComparison.OrdinalIgnoreCase) == true ? "x86" : "x64"; }
        catch { return "—"; }
    }
}
