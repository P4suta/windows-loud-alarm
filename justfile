# Common dev tasks. Run `just` for the list. Tools come from mise (.mise.toml at repo root).
set windows-shell := ["pwsh.exe", "-NoLogo", "-NoProfile", "-Command"]

default:
    @just --list

# Restore NuGet packages
restore:
    dotnet restore Alarm.slnx

# Full build with strict analyzers
build:
    dotnet build Alarm.slnx --no-restore

# Clean & rebuild
rebuild:
    dotnet build Alarm.slnx --no-incremental

# Run the alarm app (x64 Debug)
run:
    dotnet run --project src/Alarm.Presentation/Alarm.Presentation.csproj -p:Platform=x64

# Apply .editorconfig + analyzer fixes that have automatic code-fixes
format:
    dotnet format Alarm.slnx

# Check formatting without writing (CI)
format-check:
    dotnet format Alarm.slnx --verify-no-changes

# Verify clean architecture: Domain has zero references, Application only references Domain
verify-layers:
    @Write-Host "── Domain references (must be empty) ──" -ForegroundColor Cyan
    dotnet list src/Alarm.Domain/Alarm.Domain.csproj reference
    @Write-Host "── Application references (must be Domain only) ──" -ForegroundColor Cyan
    dotnet list src/Alarm.Application/Alarm.Application.csproj reference

# Publish a self-contained binary for win-x64.
# Bundles .NET 10 runtime; relies on WindowsAppRuntime 2.x being installed on the target.
# (WindowsAppSDKSelfContained=true conflicts with the MSIX-deployed runtime under WinAppSDK 2.x —
#  Microsoft.UI.Xaml.dll crashes with 0xC000027B / STATUS_STOWED_EXCEPTION at startup.)
# Output: ./publish/win-x64 (~200 MB). Zip and distribute.
publish:
    dotnet publish src/Alarm.Presentation/Alarm.Presentation.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -o publish/win-x64

# Clear the local NuGet HTTP cache (when a wildcard version refuses to resolve)
clear-cache:
    dotnet nuget locals http-cache --clear

# Remove all bin/ and obj/ directories under src/ (and publish/)
clean:
    Get-ChildItem -Path src,publish -Include bin,obj -Recurse -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

# Fresh-clone setup: install mise tools, restore NuGet packages
bootstrap:
    mise install
    just restore

# (Note: full XAML Hot Reload requires Visual Studio; this restarts the app on change.)
# Hot-reload dev loop. Edits to .cs / .xaml rebuild automatically.
watch:
    dotnet watch --project src/Alarm.Presentation/Alarm.Presentation.csproj run -p:Platform=x64

# Spell-check sources (excludes bin/obj/publish via .typos.toml or defaults)
typos:
    typos src

# Print toolchain versions used by this repo
info:
    @Write-Host "── mise tools ──" -ForegroundColor Cyan
    mise current
    @Write-Host "── dotnet SDKs ──" -ForegroundColor Cyan
    dotnet --list-sdks
    @Write-Host "── just ──" -ForegroundColor Cyan
    just --version
