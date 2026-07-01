# Contributing

Thanks for helping out. This project is a .NET 10 / WinUI 3 app with a strict,
reproducible toolchain. The short version: `just bootstrap`, make your change, keep
`just check` green, open a PR with a Conventional-Commits title.

## Setup

```sh
just bootstrap   # installs the mise-pinned toolchain + restores NuGet packages
```

Everything runs through [`just`](justfile) (which wraps the mise-pinned .NET SDK).
See `CLAUDE.md` for the full operating manual and the absolute rules (never call
`dotnet`/`mise` directly, warnings-as-errors, exhaustive reducer tests, the reducer
stays pure). Run `just` to list every recipe; `just doctor` diagnoses toolchain issues.

## Before you push

`just check` must be green — it mirrors CI: restore → build (strict analyzers) →
test → format-check. The lefthook hooks run a subset automatically:

- **commit-msg** — [committed](committed.toml) checks the Conventional-Commits format.
- **pre-commit** — `dotnet format --verify-no-changes` + typos on staged files.
- **pre-push** — the full test suite.

Lint workflow edits locally with `mise exec -- actionlint`.

## Commit & PR conventions

Commits and **PR titles** follow [Conventional Commits](https://www.conventionalcommits.org/).
The PR title is authoritative: PRs squash-merge, so the title becomes the commit that
[release-please](docs/RELEASING.md) parses to compute the next version and CHANGELOG.
The `pr-title` CI check enforces it.

Allowed types: `feat`, `fix`, `perf`, `docs`, `refactor`, `test`, `chore`, `ci`,
`build`, `deps`, `style`, `revert`. Use lowercase subjects, e.g.
`feat(alarm): add snooze` or `fix(ci): pin actionlint`.

`feat:` → minor bump, `fix:`/`perf:` → patch bump. Add a `!` or a
`BREAKING CHANGE:` footer for a major bump.

## Releases

Fully automated — you never hand-edit a version. See [docs/RELEASING.md](docs/RELEASING.md).
