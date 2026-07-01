# Common dev tasks. Run `just` for the list.
#
# Every recipe runs through `mise exec --` so the mise-pinned .NET SDK is on PATH
# even in non-interactive shells (CI, agent bash).
set windows-shell := ["mise", "exec", "--", "pwsh.exe", "-NoLogo", "-NoProfile", "-Command"]

default:
    @just --list

# Lifecycle

# Fresh-clone setup: install mise tools, restore NuGet packages
bootstrap:
    mise install
    just restore

# Restore in locked mode. Fails on Directory.Packages.props drift; re-evaluate with
# `mise exec -- dotnet restore Alarm.slnx --force-evaluate` after a bump.
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

# Hot-reload dev loop (restarts the app on change; full XAML Hot Reload needs Visual Studio).
watch:
    dotnet watch --project src/Alarm.Presentation/Alarm.Presentation.csproj run -p:Platform=x64

# Regenerate src/Alarm.Presentation/Assets/Alarm.ico from Assets/AppIcon.png. The .ico is
# committed, so re-run only after changing the source PNG or tools/make-icon.py.
icon:
    uv run --with Pillow python tools/make-icon.py

# Tests

# Build, then run each test assembly directly (MTP executables — no `dotnet test` host).
test: build test-fast

# Tests without the implicit pre-build.
test-fast:
    artifacts/bin/Alarm.Domain.Tests/debug/Alarm.Domain.Tests.exe --no-progress
    artifacts/bin/Alarm.Application.Tests/debug/Alarm.Application.Tests.exe --no-progress

# Run only the Domain tests
test-domain:
    artifacts/bin/Alarm.Domain.Tests/debug/Alarm.Domain.Tests.exe --no-progress

# Run only the Application tests
test-app:
    artifacts/bin/Alarm.Application.Tests/debug/Alarm.Application.Tests.exe --no-progress

# CI / quality

# CI-equivalent gate: restore → build → test → format-check.
check: restore build test-fast format-check

# Restore (locked) → test, for tight inner loops.
check-fast: restore test-fast

# Apply .editorconfig + analyzer auto-fixes
format:
    dotnet format Alarm.slnx

# Check formatting without writing
format-check:
    dotnet format Alarm.slnx --verify-no-changes

# Verify layering: Domain has zero references, Application only references Domain.
verify-layers:
    @Write-Host "── Domain references (must be empty) ──" -ForegroundColor Cyan
    dotnet list src/Alarm.Domain/Alarm.Domain.csproj reference
    @Write-Host "── Application references (must be Domain only) ──" -ForegroundColor Cyan
    dotnet list src/Alarm.Application/Alarm.Application.csproj reference

# Spell-check sources
typos:
    typos src

# Distribution

# Pre-publish kill, separate from stop-app so just's per-call dedup doesn't skip the
# second kill inside `full`. taskkill is non-zero when nothing matches; `;exit 0` is fine.
_kill-app-quiet:
    @taskkill /IM Alarm.exe /F /T 2>$null; exit 0

# Assemble the downloadable bundle in ./publish/dist/Alarm (see scripts/AssembleBundle.cs
# and docs/ARCHITECTURE.md). Native AOT launcher needs the MSVC C++ toolchain (`just doctor`).
publish: _kill-app-quiet
    dotnet run scripts/AssembleBundle.cs

# Zip the bundle + write SHA256SUMS.txt for a Release (run `just publish` first). Shared
# with CI's release.yml via `just package`. See scripts/Package.cs.
package TAG:
    dotnet run scripts/Package.cs -- {{TAG}}

# Kill a running Alarm.exe — it locks publish/dist/Alarm/app/Alarm.{exe,dll} against re-publish.
stop-app:
    @$p = Get-Process -Name Alarm -ErrorAction SilentlyContinue; if ($p) { Write-Host ("[stop-app] killing PID {0}" -f $p.Id) -ForegroundColor Yellow; Stop-Process -Id $p.Id -Force; Start-Sleep -Milliseconds 500 } else { Write-Host "[stop-app] no Alarm.exe running" -ForegroundColor DarkGray }

# Full release pipeline: stop → clean → restore → rebuild → test → format → publish → summary.
full: stop-app clean restore rebuild test-fast format-check publish artifact-summary

# Print the assembled bundle's layout + total footprint. See scripts/ArtifactSummary.cs.
artifact-summary:
    dotnet run scripts/ArtifactSummary.cs

# Performance & diagnostics

# Perf medians (cold rebuild / warm / test) + binlog + analyzer report. See scripts/MeasureBuild.cs.
measure *ARGS:
    dotnet run scripts/MeasureBuild.cs -- {{ARGS}}

# Build with a binary log + PerformanceSummary (open with MSBuild Binary Log Viewer).
measure-binlog:
    New-Item -ItemType Directory -Force -Path artifacts/perf | Out-Null
    dotnet build Alarm.slnx --no-restore -bl:artifacts/perf/build.binlog /clp:PerformanceSummary

# Per-analyzer time report. --no-incremental forces analyzers to run; -tl:off + detailed
# verbosity are needed for csc to print the analyzer table.
analyzer-report:
    dotnet build Alarm.slnx --no-restore --no-incremental -tl:off -p:ReportAnalyzer=true -clp:Verbosity=detailed -nologo

# MSBuild BuildCheck — structural findings about the build itself.
lint-build:
    dotnet build Alarm.slnx --no-restore -check /nologo

# Aggregate target/task/project timings from a binlog (run `just measure-binlog` first).
analyze-binlog *ARGS:
    dotnet run scripts/AnalyzeBinlog.cs -- {{ARGS}}

# Diagnostics & cleanup

# Diagnose the toolchain (mise/dotnet/just + MSVC C++ probe). See scripts/Doctor.cs.
doctor:
    dotnet run scripts/Doctor.cs

# Short toolchain version dump.
info:
    @Write-Host "── mise tools ──" -ForegroundColor Cyan
    mise current
    @Write-Host "── dotnet SDKs ──" -ForegroundColor Cyan
    dotnet --list-sdks
    @Write-Host "── just ──" -ForegroundColor Cyan
    just --version

# Remove artifacts/ and publish/. `;exit 0` tolerates an already-missing path.
clean:
    Remove-Item -Recurse -Force -ErrorAction Ignore -Path artifacts, publish; exit 0

# Clear the local NuGet HTTP cache
clear-cache:
    dotnet nuget locals http-cache --clear
