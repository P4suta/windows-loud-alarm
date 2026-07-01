<!--
PR titles must follow Conventional Commits (feat:, fix:, docs:, ci:, …) — the
pr-title check enforces it, and the title becomes the squash-merge commit that
release-please reads to compute the next version + CHANGELOG. See CONTRIBUTING.md.
-->

## What & why

<!-- What does this change do, and what problem does it solve? -->

## How verified

<!-- e.g. `just check` green, ran the app, added tests. -->

- [ ] `just check` passes (restore → build → test → format-check)
- [ ] Reducer table tests stay exhaustive if a state/event was added (CLAUDE.md rule 4)
- [ ] No new analyzer suppressions outside `.editorconfig` (CLAUDE.md rule 3)
