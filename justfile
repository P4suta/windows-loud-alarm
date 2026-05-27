# Common dev tasks. Run `just` for the list.
#
# Toolchain note: every recipe runs through `mise exec --` so the .NET SDK pinned by
# mise.toml is on PATH even from non-interactive shells (CI, agent bash, etc.). Humans
# with mise activated in their interactive shell aren't affected by the extra wrapper.
set windows-shell := ["mise", "exec", "--", "pwsh.exe", "-NoLogo", "-NoProfile", "-Command"]

default:
    @just --list

# ─────────────────────────────────────────────────────────────
# Lifecycle
# ─────────────────────────────────────────────────────────────

# Fresh-clone setup: install mise tools, restore NuGet packages
bootstrap:
    mise install
    just restore

# Restore NuGet packages in --locked-mode. Fails fast if a Directory.Packages.props
# version drifted without a matching packages.lock.json update. Re-evaluate with
# `mise exec -- dotnet restore Alarm.slnx --force-evaluate` after a package bump.
restore:
    dotnet restore Alarm.slnx --locked-mode

# Full build with strict analyzers (src + tests)
build:
    dotnet build Alarm.slnx --no-restore

# Clean & rebuild
rebuild:
    dotnet build Alarm.slnx --no-incremental

# Run the alarm app (x64 Debug)
run:
    dotnet run --project src/Alarm.Presentation/Alarm.Presentation.csproj -p:Platform=x64

# Hot-reload dev loop. Edits to .cs / .xaml rebuild automatically.
# (Note: full XAML Hot Reload requires Visual Studio; this restarts the app on change.)
watch:
    dotnet watch --project src/Alarm.Presentation/Alarm.Presentation.csproj run -p:Platform=x64

# ─────────────────────────────────────────────────────────────
# Tests
# ─────────────────────────────────────────────────────────────

# Run all tests (Domain + Application). Builds first, then invokes each test
# assembly directly via Microsoft.Testing.Platform — `dotnet test` adds ~2s of
# host startup we no longer need now that the assemblies ship as MTP executables.
test: build test-fast

# Tests without the implicit pre-build. Sequential MTP exe invocation —
# we tried wrapping these in a `dotnet run scripts/RunTests.cs` parallel
# orchestrator but the file-based-program startup ate the parallelism win.
# Each exe internally runs its xUnit collections in parallel; that's enough.
test-fast:
    artifacts/bin/Alarm.Domain.Tests/debug/Alarm.Domain.Tests.exe --no-progress
    artifacts/bin/Alarm.Application.Tests/debug/Alarm.Application.Tests.exe --no-progress

# Run only the Domain tests
test-domain:
    artifacts/bin/Alarm.Domain.Tests/debug/Alarm.Domain.Tests.exe --no-progress

# Run only the Application tests
test-app:
    artifacts/bin/Alarm.Application.Tests/debug/Alarm.Application.Tests.exe --no-progress

# ─────────────────────────────────────────────────────────────
# CI / quality
# ─────────────────────────────────────────────────────────────

# CI-equivalent: restore → build (strict analyzers) → test → format-check.
# A single command that must stay green before you call a change "done".
# Note: uses `test-fast` (which is just the MTP exe invocations) because `test`
# would re-run `build` and double the cost.
check: restore build test-fast format-check

# Restore (locked) → test (with implicit build). For tight inner loops where
# you trust the lock file and don't need an explicit format gate.
check-fast: restore test-fast

# Apply .editorconfig + analyzer fixes that have automatic code-fixes
format:
    dotnet format Alarm.slnx

# Check formatting without writing (CI gate inside `just check`)
format-check:
    dotnet format Alarm.slnx --verify-no-changes

# Verify clean architecture: Domain has zero references, Application only references Domain
verify-layers:
    @Write-Host "── Domain references (must be empty) ──" -ForegroundColor Cyan
    dotnet list src/Alarm.Domain/Alarm.Domain.csproj reference
    @Write-Host "── Application references (must be Domain only) ──" -ForegroundColor Cyan
    dotnet list src/Alarm.Application/Alarm.Application.csproj reference

# Spell-check sources
typos:
    typos src

# ─────────────────────────────────────────────────────────────
# Distribution
# ─────────────────────────────────────────────────────────────

# Internal pre-publish kill: even after full's first stop-app, the user might have launched
# the binary again during the long build phase. This is a separate recipe (not stop-app
# itself) so just's "dependencies run once per call" rule doesn't dedup the second kill.
# taskkill returns non-zero when there's nothing to kill; `;exit 0` keeps just happy.
_kill-app-quiet:
    @taskkill /IM Alarm.exe /F /T 2>$null; exit 0

# Publish a self-contained binary for win-x64.
# Bundles .NET 10 runtime; relies on WindowsAppRuntime 2.x being installed on the target.
# (WindowsAppSDKSelfContained=true conflicts with the MSIX-deployed runtime under WinAppSDK 2.x —
#  Microsoft.UI.Xaml.dll crashes with 0xC000027B / STATUS_STOWED_EXCEPTION at startup.)
# Output: ./publish/win-x64 (~200 MB). Zip and distribute.
publish: _kill-app-quiet
    dotnet publish src/Alarm.Presentation/Alarm.Presentation.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -o publish/win-x64

# Kill a running Alarm.exe if any — required before re-publishing, because the running
# process holds a lock on publish/win-x64/Alarm.{exe,dll} that would make `dotnet publish` fail.
stop-app:
    @$p = Get-Process -Name Alarm -ErrorAction SilentlyContinue; if ($p) { Write-Host ("[stop-app] killing PID {0}" -f $p.Id) -ForegroundColor Yellow; Stop-Process -Id $p.Id -Force; Start-Sleep -Milliseconds 500 } else { Write-Host "[stop-app] no Alarm.exe running" -ForegroundColor DarkGray }

# Full release pipeline — one command for "I want a shippable build I trust".
# stop-app → clean → restore → Debug rebuild → all 52 tests → format gate → Release publish → artifact summary.
# Roughly 40 s on a warm NuGet cache; that's "sakusaku" enough to run before every PR.
full: stop-app clean restore rebuild test-fast format-check publish artifact-summary

# Print the published binary's size and total publish/ footprint. Standalone so you can
# rerun it without rebuilding when you just want to see what's currently on disk.
artifact-summary:
    @Write-Host ""
    @Write-Host "── publish/win-x64 ──" -ForegroundColor Cyan
    @if (Test-Path publish/win-x64/Alarm.exe) { Get-Item publish/win-x64/Alarm.exe | Format-Table Name, Length, LastWriteTime -AutoSize } else { Write-Host "  (no published binary — run 'just publish' or 'just full' first)" -ForegroundColor Yellow }
    @if (Test-Path publish/win-x64) { $sum = Get-ChildItem publish/win-x64 -Recurse -File | Measure-Object -Property Length -Sum; Write-Host ("  Total: {0:N1} MB across {1} files" -f ($sum.Sum / 1MB), $sum.Count) }

# ─────────────────────────────────────────────────────────────
# Performance measurement & diagnostics
# ─────────────────────────────────────────────────────────────

# Run the perf measurement script (.NET 10 file-based program): cold rebuild / warm
# incremental / test medians, binlog, analyzer report, BuildCheck. Writes
# docs/perf/baseline-<date>.md.
measure *ARGS:
    dotnet run scripts/MeasureBuild.cs -- {{ARGS}}

# Build with a binary log + PerformanceSummary (target-level timings).
# Open the .binlog with MSBuild Binary Log Viewer for drilldown.
measure-binlog:
    New-Item -ItemType Directory -Force -Path artifacts/perf | Out-Null
    dotnet build Alarm.slnx --no-restore -bl:artifacts/perf/build.binlog /clp:PerformanceSummary

# Per-analyzer time report. Use to spot which Roslyn analyzers dominate.
# --no-incremental: force CoreCompile to actually run analyzers.
# -tl:off:          disable the terminal logger (it strips csc's analyzer summary).
# -clp:Verbosity=d: csc.exe writes the analyzer table only at detailed console verbosity.
analyzer-report:
    dotnet build Alarm.slnx --no-restore --no-incremental -tl:off -p:ReportAnalyzer=true -clp:Verbosity=detailed -nologo

# MSBuild BuildCheck — structural / anti-pattern findings about the build itself.
# Exits non-zero when findings are emitted as errors; that's surfaced verbatim.
lint-build:
    dotnet build Alarm.slnx --no-restore -check /nologo

# Aggregate target / task / project elapsed time from artifacts/perf/build.binlog.
# Run `just measure` (or `just measure-binlog`) first to produce a binlog.
analyze-binlog *ARGS:
    dotnet run scripts/AnalyzeBinlog.cs -- {{ARGS}}

# ─────────────────────────────────────────────────────────────
# Diagnostics & cleanup
# ─────────────────────────────────────────────────────────────

# Diagnose the toolchain. Run this first when something is off.
doctor:
    @Write-Host "── mise current ──" -ForegroundColor Cyan
    mise current
    @Write-Host "── mise installed tools ──" -ForegroundColor Cyan
    mise ls --installed
    @Write-Host "── dotnet SDKs visible to mise ──" -ForegroundColor Cyan
    dotnet --list-sdks
    @Write-Host "── just version ──" -ForegroundColor Cyan
    just --version

# Print toolchain versions used by this repo (legacy alias of `doctor`'s short form)
info:
    @Write-Host "── mise tools ──" -ForegroundColor Cyan
    mise current
    @Write-Host "── dotnet SDKs ──" -ForegroundColor Cyan
    dotnet --list-sdks
    @Write-Host "── just ──" -ForegroundColor Cyan
    just --version

# Remove the unified artifacts/ tree (UseArtifactsOutput) and the publish/ output
# folder. publish/ is the distribution drop, intentionally separate from artifacts/.
# `;exit 0` keeps just happy when one of the paths is already gone.
clean:
    Remove-Item -Recurse -Force -ErrorAction Ignore -Path artifacts, publish; exit 0

# Clear the local NuGet HTTP cache (when a wildcard version refuses to resolve)
clear-cache:
    dotnet nuget locals http-cache --clear
