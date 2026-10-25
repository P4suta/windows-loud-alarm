# Alarm guide

Alarm is a Windows desktop alarm clock built with .NET 10 and WinUI 3.
Press and hold to arm the alarm or cancel an armed alarm.
When the alarm rings, Alarm raises the system master volume to 100% and loops the sound.
Press and hold `STOP` to stop playback and restore the captured volume.
The window displays a countdown until the alarm rings.

## Run a release

The v0.1.0 release provides an unpackaged Windows x64 ZIP with the .NET runtime included.
Install the x64 [Windows App Runtime 2.x](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) before launching Alarm.
Download [Alarm-v0.1.0-win-x64.zip](https://github.com/P4suta/windows-loud-alarm/releases/download/v0.1.0/Alarm-v0.1.0-win-x64.zip), unpack the archive, and double-click `Alarm.exe`.
Keep the unpacked files together.
The [root README](../README.md) provides commands to download, unpack, and start the release from Windows PowerShell.

Current source builds use a launcher at the bundle root and place the app's runtime files in `app/`.
The [distribution layout](ARCHITECTURE.md#distribution-layout) describes the launcher and its build requirements.

## Develop on Windows

Install Git, [mise](https://mise.jdx.dev/), and PowerShell 7 on Windows x64.
The project recipes require `pwsh.exe` on PATH.
Clone the repository, trust its tool configuration, and install its tools before restoring packages and running the app.

```powershell
git clone https://github.com/P4suta/windows-loud-alarm.git
cd windows-loud-alarm
mise trust
mise install
mise x -- just restore
mise x -- just run
```

`global.json` declares the .NET software development kit (SDK) version.
Mise reads that file when selecting and installing the SDK.
`mise.toml` declares the remaining tool versions.
`Directory.Packages.props` declares package versions, and `.editorconfig` defines analyzer rules.
After setup, `mise x -- just bootstrap` installs the configured tools and restores packages.
The [justfile](../justfile) defines the development commands.

## Build and test

Run `mise x -- just build` to compile the solution with strict analyzers.
Run `mise x -- just test` to build and execute the `Alarm.Domain.Tests` and `Alarm.Application.Tests` assemblies.
The tests use xUnit and Shouldly, with fakes in `tests/Alarm.Application.Tests/Fakes/`.
Reducer table tests cover state and event combinations.
After a build, `mise x -- just test-domain` and `mise x -- just test-app` run the corresponding test assemblies.

Run `mise x -- just check` before pushing.
The command restores locked packages, builds, runs tests, and verifies formatting.
`Directory.Build.props` treats warnings as errors and enables all analyzer rules.
Project suppression guidance is in [CONTRIBUTING.md](../CONTRIBUTING.md#before-you-push).

Use `mise x -- just format` to apply supported formatting fixes and `mise x -- just watch` to restart the app when source changes.
`mise x -- just doctor` diagnoses the configured toolchain.
`mise x -- just clean` removes the generated `artifacts/` and `publish/` directories.
Run `mise x -- just` to list the available recipes.

## Architecture

The Domain layer holds value objects and result types.
The `Alarm.Application` project holds the reducer, store, events, effects, and ports.
The Infrastructure layer provides audio playback, volume control, clocks, and audio resolution.
The Presentation layer provides the WinUI window, view model, long-press behavior, and tray status.
The launcher is a separate project used by the portable bundle.

The store sends each event through the reducer to produce the next state and effects.
The effect interpreter performs audio and volume operations, then dispatches the resulting events back to the store.
The [architecture guide](ARCHITECTURE.md) explains the boundaries and distribution layout.

```text
src/Alarm.Domain          Value objects and result types
src/Alarm.Application     Reducer, store, effects, services, and ports
src/Alarm.Infrastructure  Audio, volume, clock, and audio resolution
src/Alarm.Presentation    WinUI window, view model, behaviors, and tray
src/Alarm.Launcher        Portable bundle entry point
tests/                   Domain and Application tests and fakes
docs/                    Architecture, signing, and release guides
Alarm.slnx               Solution for the app and tests
```

## Package and release

`mise x -- just publish` assembles the current source bundle in `publish/dist/Alarm/`.
`mise x -- just package vX.Y.Z` creates a ZIP and `SHA256SUMS.txt` from an existing bundle.
`mise x -- just full` stops a running Alarm process, cleans outputs, restores packages, rebuilds, runs tests, checks formatting, and publishes the bundle.
Publishing the launcher requires the Microsoft C++ build tools.

The release-please workflow derives version proposals from Conventional Commits.
The [release guide](RELEASING.md) describes the release approval gates, checksums, build provenance, and software bill of materials.
The [signing guide](SIGNING.md) describes executable signing and verification.
Contribution and commit conventions are in [CONTRIBUTING.md](../CONTRIBUTING.md).

## License

Personal project, no warranty.

The bundled Geist and Geist Mono fonts use the existing [font license](../src/Alarm.Presentation/Assets/Fonts/OFL.txt).
