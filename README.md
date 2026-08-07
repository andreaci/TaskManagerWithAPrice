# TaskCost

A C# / WPF Windows process manager inspired by the Windows 11 Task Manager. In addition to live process data, TaskCost estimates the market value of each process's working-set memory.

## Run

```powershell
dotnet run
```

## Memory-value calculation

`working set bytes / 1 GiB × selected DDR price in EUR/GB × selected currency rate`

Prices for DDR3, DDR4, and DDR5 are editable in Settings. Currency choices include EUR, USD, GBP, CHF, JPY, CAD, and AUD, with an editable conversion rate. Downloaded market data and custom values are saved under `%LOCALAPPDATA%\TaskCost\market-data.json`.

TaskCost checks RAMTrack and the European Central Bank at most once per UTC day and reuses the cached values offline. Editing DDR4/DDR5 prices or a conversion rate disables automatic updates for that data source. **Download latest and clear custom** clears both custom locks and immediately refreshes both services. DDR3 is always user-supplied because RAMTrack does not publish a DDR3 price.

TaskCost is a read-only process monitor: it does not terminate, suspend, reprioritize, or otherwise modify processes. At startup it offers to restart through Windows UAC as administrator so protected process metadata can be displayed when permitted.

RAMTrack refresh reads the site's current, undocumented `/api/prices` endpoint and averages its per-GB capacity buckets for DDR4 and DDR5. Manual values always remain available if that endpoint changes.

## Continuous builds and releases

GitHub Actions builds the application after every push and pull request. Each successful run retains a self-contained Windows x64 ZIP as a workflow artifact for 14 days.

Include a semantic version such as `v1.0.0` in the latest commit message on the repository's default branch. After the build passes, GitHub Actions creates the tag and a GitHub Release with generated notes and the packaged application attached:

```powershell
git commit -m "Release v1.0.0"
git push
```

Prerelease versions such as `v1.1.0-beta.1` are also recognized. Manually pushing a `v*` tag remains supported. Existing version tags are never moved; the release fails safely if the requested tag already belongs to another commit.
