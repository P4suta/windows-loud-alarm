# Contributing

## Setup

`just bootstrap` installs the mise-pinned toolchain and restores packages. Everything
runs through [`just`](justfile) (`just` lists recipes, `just doctor` diagnoses
toolchain issues). See [`README.md`](README.md) for the overview and
[`CLAUDE.md`](CLAUDE.md) for the operating manual and absolute rules (never call
`dotnet`/`mise` directly, warnings-as-errors, exhaustive reducer tests, pure reducer).

## Before you push

`just check` must be green — it mirrors CI: restore → build (strict analyzers) →
test → format-check. The lefthook hooks run a subset automatically:

- **commit-msg** — [committed](committed.toml) checks the Conventional-Commits format.
- **pre-commit** — `dotnet format --verify-no-changes` + typos on staged files.
- **pre-push** — the full test suite.

Lint workflow edits locally with `mise exec -- actionlint`.

## Commit & PR conventions

Commits and **PR titles** follow [Conventional Commits](https://www.conventionalcommits.org/).
PRs squash-merge, so the title becomes the commit that
[release-please](docs/RELEASING.md) parses for the next version and CHANGELOG. The
`pr-title` CI check enforces it.

Allowed types: `feat`, `fix`, `perf`, `docs`, `refactor`, `test`, `chore`, `ci`,
`build`, `deps`, `style`, `revert`. Use lowercase subjects, e.g.
`feat(alarm): add snooze` or `fix(ci): pin actionlint`.

`feat:` → minor bump, `fix:`/`perf:` → patch bump. Add a `!` or a
`BREAKING CHANGE:` footer for a major bump.

## Releases

Fully automated — you never hand-edit a version. See [docs/RELEASING.md](docs/RELEASING.md).
