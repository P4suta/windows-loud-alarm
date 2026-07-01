# 0001 — Automated versioning with release-please

- Status: accepted
- Date: 2026-07-01

## Context

The project had mature build quality (strict analyzers, Central Package Management,
NuGet lockfiles, lefthook, deterministic builds) but **no release axis at all**: no
tags, no `CHANGELOG.md`, no version strategy, no release automation. Commits already
followed Conventional Commits and merged via numbered PRs — the ideal input for an
automated release tool. We wanted "cut a release" to be a deliberate, low-ceremony act
that produces a versioned GitHub Release with a verifiable artifact, without hand-
editing versions.

This design ports the hard-won lessons from a sibling project (`find-my-files`) that
brought up release-please on a dual-language Rust+C# repo, adapted to this **single-
language C#** repo — which drops most of the complexity.

## Decision

Adopt **release-please** with `release-type: "simple"` at the repo root, driving:

- **`version.txt`** — the `simple` strategy's canonical version file.
- **`Directory.Build.props`** — a `generic` extra-files updater keyed on the
  `<!-- x-release-please-version -->` annotation bumps `<Version>`, from which
  `AssemblyVersion`/`FileVersion`/`InformationalVersion` derive. So `Alarm.exe`'s file
  properties always match the shipped tag.

Release model: **draft-first** (`"draft": true` + `"force-tag-creation": true`).
release-please cuts the tag and a *draft* Release on merge, then dispatches
`release.yml`, which attaches assets and publishes — the asset-before-publish order
GitHub Immutable Releases require. Authentication is a **GitHub App** (dormant-first:
green no-op until secrets are set), because only an App/PAT push can trigger the
downstream build workflow.

## Why `simple` (not a .NET-native release-type)

release-please has no C#/.NET release strategy that bumps a csproj/props version
natively. `simple` + the `generic` updater's annotation trick is the portable path,
and it's the same mechanism the sibling repo used for its csproj.

## What we deliberately left out (vs. the sibling repo)

- **No Cargo.lock sync / toml updater / Contents-API commit.** Single-language: the
  bumped `<Version>` doesn't touch `packages.lock.json`, so there's no post-bump lock
  sync at all. This is the biggest simplification.
- **No Rust-specific workflows** (fuzz, mutants, cargo-audit) — replaced where relevant
  by NuGet equivalents (`nuget-audit.yml`, CycloneDX .NET SBOM).

Code signing **is** included: `release.yml` is a build → sign → publish three-stage,
Authenticode-signing our five first-party PEs with SSL.com eSigner (the same provider/
Action as the sibling repo), hard-gated so a real publish can't ship unsigned. See
`docs/SIGNING.md`.

## Consequences

- Versions and `CHANGELOG.md` are derived purely from Conventional Commits; humans
  never edit a version. The `pr-title` CI check + local `committed` hook keep commit
  messages well-formed.
- A layered safety-gate suite (`release-gate`, `no-automerge-on-release-pr`,
  `release-label-guard`, two-environment approvals, dispatch-only release) makes cutting
  a release a deliberate act.
- The first release is pinned to **v0.1.0** via `release-as`, which must be removed
  after it ships (see `docs/RELEASING.md`).

## Re-examination triggers

- A code-signing certificate becomes available → add the `sign` job.
- ARM64 or MSIX distribution is wanted → extend `just package` + `release.yml`.
- release-please ships a first-class .NET release-type → reconsider `simple`.
