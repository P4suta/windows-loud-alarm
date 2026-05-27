# Alarm — agent operating manual

This file is the **single source of truth** for working in this repo. Read it once at
session start and follow the rules. Do not invent alternative workflows.

## Quickstart

Run these from the repo root. Every recipe in `justfile` already wraps `dotnet`
through `mise exec --` so the pinned .NET SDK is on PATH even if your shell never
activated mise.

| Want to… | Run |
|---|---|
| Set up a fresh clone | `just bootstrap` |
| Build everything (src + tests) | `just build` |
| Run all 52 tests | `just test` |
| Run the desktop app | `just run` |
| CI-equivalent verification | `just check` |
| Full release pipeline (clean → rebuild → test → publish) | `just full` |
| Diagnose toolchain problems | `just doctor` |
| List every recipe | `just` |

If `just build` fails with "SDK not found", run `just bootstrap` and try again. The
mise tool list lives in `mise.toml` and pins **.NET SDK 10.0.300**.

## Absolute rules

These are non-negotiable. If a rule is wrong, change the rule first — don't bypass it.

1. **Never invoke `dotnet` directly.** Use `just <recipe>`. If the operation you need
   isn't covered, add a recipe to `justfile` first, then call it.
2. **Never invoke `mise` directly for build-related work.** Only `just bootstrap`
   should call `mise install`. Toolchain inspection: `just doctor`.
3. **`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` + `AnalysisMode=All`** in
   `Directory.Build.props`. Every analyzer warning is a build break. If a suppression
   is genuinely warranted, add it to `.editorconfig` **with a comment explaining why**.
   Do not put `<NoWarn>` in `.csproj` files. Production code suppressions and test-only
   suppressions live in clearly separated sections of `.editorconfig`.
4. **Reducer table tests stay exhaustive.** `tests/Alarm.Application.Tests/Reducer/AlarmReducerTests.cs`
   must contain a row for every (state × event) pair. If you add a state or an event,
   add the corresponding rows in the same change.
5. **The state machine is a pure function.** `AlarmReducer.Reduce(state, event, now)`
   takes no services and produces `(state', effects[])`. Side effects are emitted as
   `AlarmEffect` values that the interpreter executes — never call ports from inside
   the reducer.

## Directory map

```
src/Alarm.Domain         Pure value types, Result<T,E>, AlarmState/Event/Effect, no external deps
src/Alarm.Application    Reducer, Store (Channel + R3 BehaviorSubject), EffectInterpreter,
                         BackgroundServices, Ports (IClock, IAudioPlayer, ...)
src/Alarm.Infrastructure NAudio playback, CoreAudio volume, TimeProvider-backed clock
src/Alarm.Presentation   WinUI 3 window, MainViewModel (Store projection),
                         LongPressGestureBehavior, TrayStatusPresenter
tests/Alarm.Domain.Tests       28 boundary-value tests for value objects
tests/Alarm.Application.Tests  Reducer table tests + AlarmStore scenarios with fake ports
docs/ARCHITECTURE.md     Deep dive: dataflow, invariants, library trade-offs
```

## Toolchain

- **mise** (`mise.toml`): pins .NET SDK 10.0.300. Lives at the repo root.
- **just** (`justfile`): every command goes through `mise exec --` so the recipe shell
  inherits mise's PATH/env even in non-interactive contexts (CI, agent bash).
- **Central Package Management** (`Directory.Packages.props`): every NuGet version is
  declared in one file. Add a new package with `just add-package <name>` if you write
  such a recipe, or directly via `mise exec -- dotnet add` (this is the one carve-out
  in rule 1 — adding NuGet packages can stay ad-hoc).

## Further reading

- `README.md` — overview, screenshot, contributor onboarding for humans.
- `docs/ARCHITECTURE.md` — single-direction dataflow, type-level invariants,
  the rationale for libraries adopted/rejected.

## Optional: team-wide Claude Code permission rules

`.claude/settings.local.json` (per-user, gitignored) already allows `just *`, `mise install`,
`mise ls`, `mise current`, and `mise exec -- dotnet *`. To make these the team default
(committed to git), create `.claude/settings.json` with the same `permissions.allow`
array. Claude Code's auto mode forbids self-writing `settings.json`, so a human has to
create that file manually — the user-level deny rule blocks even me.
