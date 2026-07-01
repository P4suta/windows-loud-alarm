# Perf measurement

Build/test performance baselines for the Alarm repo, so speed claims come with numbers.

## How to measure

```pwsh
just measure                                      # medians into baseline-<date>.md
just measure --iterations 7 --label "after lock-file"
```

`just measure` runs `scripts/MeasureBuild.cs` (a .NET 10 file-based program). `--label`
becomes a filename slug (`baseline-<date>-after-lock-file.md`) so per-step runs coexist;
without it the script writes the canonical `baseline-<date>.md`.

Binlog diagnostics:

```pwsh
just measure-binlog     # → artifacts/perf/build.binlog (+ PerformanceSummary)
just analyze-binlog     # aggregate target/task/project timings from that binlog
```

## What gets measured

Three timed scenarios, median over `--iterations` runs (default 5):

| Scenario           | Pipeline                                       | What it tells you |
|--------------------|------------------------------------------------|-------------------|
| `cold-rebuild`     | `just clean` → `just restore` → `just rebuild` | Full `git clean -xfd` cost: restore + full compile + analyzers. |
| `warm-incremental` | `just build` (no source changes)               | The "I hit Save" cost. Should be sub-second if MSBuild incremental works. |
| `test-fast`        | `just test-fast` (no implicit build)           | Test execution alone. |

Plus one-shot diagnostic captures under `artifacts/perf/` (gitignored):

- **`build.binlog`** — MSBuild binary log. Open with the
  [MSBuild Binary Log Viewer](https://msbuildlog.com/) for target-level timings.
- **`perf-summary.txt`** — `/clp:PerformanceSummary` (top targets and tasks).
- **`analyzer-report.txt`** — per-Roslyn-analyzer time (`-p:ReportAnalyzer=true`).
- **`build-check.txt`** — MSBuild `-check` (BuildCheck) structural findings.

## Reading the numbers

- Median over 5 runs, not mean.
- Same machine/conditions only — the markdown footer records host, CPU, OS, .NET version.
- One change at a time: measure before, apply, measure after, record the diff.
- `warm-incremental` should be sub-second; if not, something is invalidating the build
  cache — check the binlog.
