# Contributing

## Setup

Follow the [Windows setup guide](docs/README.md#develop-on-windows) to install the tools and restore packages.
Use the [justfile](justfile) recipes for development operations.
`mise x -- just` lists the recipes, and `mise x -- just doctor` diagnoses toolchain problems.

## Before you push

Run `mise x -- just check` before pushing.
The command restores packages, builds with strict analyzers, executes the tests, and verifies formatting.
Keep the reducer pure and keep its state and event table tests exhaustive when behavior changes.
Put analyzer suppressions in `.editorconfig` with an explanation.
Separate the production and test sections.
Add no new suppressions to project files.

The commit message hook checks [Conventional Commits](https://www.conventionalcommits.org/) with [committed](committed.toml).
The pre-commit hooks check formatting for staged source files and spelling for staged text.
The pre-push hook runs the full test suite.
Lint workflow edits with `mise x -- actionlint`.

## Commit and pull request conventions

Use Conventional Commits for commit messages and pull request titles, with lowercase subjects.
The allowed types are `feat`, `fix`, `perf`, `docs`, `refactor`, `test`, `chore`, `ci`, `build`, `deps`, `style`, and `revert`.
Examples include `feat(alarm): add snooze` and `fix(ci): pin actionlint`.

Pull requests use squash merges, so the title becomes the commit that release-please parses for the version and changelog.
The `pr-title` check enforces the title format.
`feat:` produces a minor version increase, while `fix:` and `perf:` produce a patch increase.
Use `!` or a `BREAKING CHANGE:` footer for a major increase.

## Releases

The release-please workflow automates version proposals.
The [release guide](docs/RELEASING.md) describes the approval and publication steps.
