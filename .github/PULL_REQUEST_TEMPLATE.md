<!--
PR titles must follow Conventional Commits (feat:, fix:, docs:, ci:, …) — the
pr-title check enforces it, and the title becomes the squash-merge commit that
release-please reads to compute the next version + CHANGELOG. See CONTRIBUTING.md.
-->

## What & why

<!-- What does this change do, and what problem does it solve? -->

## How verified

<!-- e.g. `just check` green, ran the app, added tests. -->

- [ ] Run `just check` and confirm that restore, build, tests, and format-check pass.
- [ ] Keep reducer table tests exhaustive after adding any state or event.
- [ ] Keep new analyzer suppressions inside `.editorconfig`.
