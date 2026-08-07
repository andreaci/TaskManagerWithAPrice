# TaskCost

A C# / WPF Windows process manager inspired by the Windows 11 Task Manager. In addition to live process data, TaskCost estimates the market value of each process's working-set memory.

## Run

```powershell
dotnet run
```

## Memory-value calculation

`working set bytes / 1 GiB × selected DDR price in EUR/GB × selected currency rate`

Prices for DDR3, DDR4, and DDR5 are editable in Settings. Currency choices include EUR, USD, GBP, CHF, JPY, CAD, and AUD, with an editable conversion rate. Settings are saved under `%LOCALAPPDATA%\TaskCost\settings.json`.

RAMTrack refresh reads the site's current, undocumented `/api/prices` endpoint and averages its per-GB capacity buckets for DDR4 and DDR5. Manual values always remain available if that endpoint changes.

## Continuous builds and releases

GitHub Actions builds the application after every push and pull request. Each successful run retains a self-contained Windows x64 ZIP as a workflow artifact for 14 days.

Push a version tag to create a GitHub Release with generated notes and the packaged application attached:

```powershell
git tag v1.0.0
git push origin v1.0.0
```
