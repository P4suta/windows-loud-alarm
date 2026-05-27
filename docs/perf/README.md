# Perf measurement

This directory holds **build/test performance baselines** for the Alarm repo so we can
say "X% faster" with numbers instead of vibes.

## How to measure

```pwsh
just measure
```

That's it. The command runs `scripts/MeasureBuild.cs` (a .NET 10 file-based program)
which produces `baseline-<yyyy-MM-dd>.md` in this directory.

Custom run:

```pwsh
just measure --iterations 7 --label "after lock-file"
```

`--label` becomes a filename slug (`baseline-<date>-after-lock-file.md`), so per-step
runs coexist instead of overwriting each other. Without `--label`, the script writes
to the canonical `baseline-<date>.md` — use that for the rolled-up summary, and
labeled runs for the individual before/after pairs.

## What gets measured

Three timed scenarios, median over `--iterations` runs (default 5):

| Scenario           | Pipeline                                     | What it tells you                |
|--------------------|----------------------------------------------|----------------------------------|
| `cold-rebuild`     | `just clean` → `just restore` → `just rebuild` | The "I just `git clean -xfd`" cost. NuGet restore + full compile + analyzers. |
| `warm-incremental` | `just build` (no source changes)             | The "I hit Save" cost. Should be very small if MSBuild incremental works.    |
| `test-fast`        | `just test-fast` (no implicit build)         | Test execution alone, no rebuild noise.                                       |

Plus three one-shot diagnostic captures:

- **`artifacts/perf/build.binlog`** — MSBuild binary log. Open with the
  [MSBuild Binary Log Viewer](https://msbuildlog.com/) to drill into target-level timings
  and find slow MSBuild tasks.
- **`artifacts/perf/perf-summary.txt`** — `/clp:PerformanceSummary` (top targets and tasks).
- **`artifacts/perf/analyzer-report.txt`** — per-Roslyn-analyzer time
  (`-p:ReportAnalyzer=true`). The summary's "Top analyzers by time" section reads from this.
- **`artifacts/perf/build-check.txt`** — MSBuild `-check` (BuildCheck) findings.
  Structural anti-patterns about the build itself (not the C# code).

`artifacts/` is gitignored. Only `docs/perf/baseline-*.md` is committed.

## How to read the numbers

- **Median, not mean.** A median over 5 runs ignores one cold/warm outlier on either side.
- **Same machine, same conditions.** The markdown footer records the host, CPU, OS, and
  .NET version — different hardware ≠ comparable numbers.
- **One change at a time.** Run `just measure` (before), apply the change, run again
  (after), record the diff in the same baseline file under a "Result" section.
- **Warm-incremental should be sub-second.** If it isn't, something is invalidating the
  build cache (a `BeforeBuild` target, file timestamps, etc.) — check the binlog.

## Workflow when optimizing

1. `just measure --label "before <change>"` → commit the resulting baseline-md.
2. Apply the change.
3. `just check` — make sure nothing is broken.
4. `just measure --label "after <change>"` → write a new baseline-md.
5. Append a "Result" section to the *after* file with the % delta vs the *before* file.

Don't delete old baselines. They are the historical record of how the build evolved.
