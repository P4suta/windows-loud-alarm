# Releasing

Releases are fully automated from [Conventional Commits](https://www.conventionalcommits.org/)
by [release-please](https://github.com/googleapis/release-please). You never hand-pick
or hand-edit a version. This document is the operator guide: how a release happens,
the safety gates, and the **one-time GitHub App setup** that activates the pipeline.

Design rationale (why `simple` + a generic updater, why draft-first, what we
deliberately left out) is recorded in
[`docs/adr/0001-automated-versioning-with-release-please.md`](adr/0001-automated-versioning-with-release-please.md).

## How a release happens

1. **Commits land on `main`.** `feat:` → minor bump, `fix:`/`perf:` → patch, a `!` or
   `BREAKING CHANGE:` footer → major. PRs squash-merge, so the **PR title** is the
   commit release-please parses (the `pr-title` CI check enforces its format).
2. **release-please keeps a Release PR open** (`.github/workflows/release-please.yml`).
   It bumps the version in `Directory.Build.props` (the `x-release-please-version`
   annotation) and `version.txt`, and regenerates `CHANGELOG.md`. It's labelled
   `autorelease: pending`.
3. **A human approves and merges.** Add the `release: approved` label (the
   `release-gate` CI check blocks merge without it), then merge by hand. Auto-merge is
   auto-disabled on Release PRs (`no-automerge-on-release-pr.yml`), and the
   `autorelease: pending` label is auto-restored if stripped (`release-label-guard.yml`).
4. **release-please cuts the tag + a DRAFT Release** (config `draft: true`,
   `force-tag-creation: true`) and dispatches `release.yml`.
5. **`release.yml` builds → signs → publishes.** The `build` job runs `just publish`
   and generates a CycloneDX SBOM. The `sign` job — gated on a manual approval in the
   `release` environment — Authenticode-signs our five first-party PEs with SSL.com
   eSigner and verifies them. The `publish` job — a *second* `release` approval —
   re-verifies the signatures (hard gate), runs `just package vX.Y.Z` (zip +
   `SHA256SUMS.txt`), writes keyless Sigstore build-provenance + SBOM attestations,
   attaches the assets to the draft, and **publishes** it (which is what makes the
   Release public). Assets are attached *before* publish, the order GitHub Immutable
   Releases require. See [`docs/SIGNING.md`](SIGNING.md).

Verify a downloaded asset:

```sh
gh attestation verify <zip> --repo P4suta/windows-loud-alarm
sha256sum -c SHA256SUMS.txt
# On Windows, also: Get-AuthenticodeSignature Alarm.exe
```

## Safety gates (defence in depth)

- **`release-gate`** (CI) — a Release PR can't be merged without the `release: approved` label.
- **`no-automerge-on-release-pr`** — auto-merge is turned back off on Release PRs, so a
  release is always a deliberate manual merge.
- **`release-label-guard`** — restores `autorelease: pending` if removed from an *open*
  Release PR (removing it would silently skip the release).
- **Dispatch-only `release.yml`** — a stray `git push vX.Y.Z` starts nothing; only
  release-please dispatches it.
- **Two environments** — `release-please` (unattended, `main`-only secrets) and
  `release` (human approval before the irreversible publish).

## One-time activation: the GitHub App

Until the App secrets are set, `release-please.yml` runs green and **no-ops** (dormant-
first) — the scaffolding is committed now; this lights it up. A GitHub App token is
used (not `GITHUB_TOKEN`) because only an App/PAT push can trigger the downstream
`release.yml`, and its API commits satisfy the signed-commits ruleset.

The **`release-please` and `release` environments are already created** (branch policy
+ reviewer set — see "Environments already provisioned" below). Only the secrets remain,
and only you can add those:

1. **Create a GitHub App** (org or personal): permissions **Contents: Read & write**
   and **Pull requests: Read & write**. No webhook needed.
2. **Install it** on the `windows-loud-alarm` repo.
3. **Generate a private key** for the App (downloads a `.pem`).
4. **Add two secrets to the `release-please` environment** (Settings → Environments →
   `release-please` → Environment secrets):
   - `RELEASE_PLEASE_CLIENT_ID` — the App's **Client ID** (not the numeric App ID).
   - `RELEASE_PLEASE_PRIVATE_KEY` — the full `.pem` contents.
5. **Add the SSL.com eSigner secrets to the `release` environment** — `ES_USERNAME`,
   `ES_PASSWORD`, `CREDENTIAL_ID`, `ES_TOTP_SECRET`. See [`docs/SIGNING.md`](SIGNING.md).

> A fine-grained **PAT** with the same Contents + Pull-requests scopes is a drop-in
> fallback for the App: set it as both `RELEASE_PLEASE_*` secrets' source, or adapt
> `release-please.yml` to read a single `token`. The App is preferred (short-lived
> tokens, not tied to a person).

### Environments already provisioned

Created for you (idempotent — safe to re-run):

- **`release-please`** — deployment branch policy **`main` only**, **no reviewers**
  (release-please runs unattended).
- **`release`** — **required reviewer: you** (the approval gate; `release.yml`'s `sign`
  and `publish` jobs each stop for it — two approvals per release, by design).

## Apply the branch rulesets (one-time)

The rulesets are committed as code under `.github/rulesets/` but must be applied to the
repo once (Settings → Rules → Rulesets → *Import*, or via `gh api`):

```sh
gh api --method POST repos/P4suta/windows-loud-alarm/rulesets \
  --input .github/rulesets/protect-default-branch.json
gh api --method POST repos/P4suta/windows-loud-alarm/rulesets \
  --input .github/rulesets/require-signed-commits.json
```

> **`require-signed-commits` requires your local git to sign commits** (gpg or ssh:
> `git config commit.gpgsign true` + a configured signing key). release-please's own
> commits are GitHub-signed (App/API), so they satisfy it automatically. If you don't
> want to sign locally yet, skip that ruleset — the rest of the pipeline is unaffected.

## First release

The first release is pinned to **v0.1.0** via `"release-as": "0.1.0"` in
`release-please-config.json`, with the manifest seeded at `0.0.0` and `bootstrap-sha`
at the adoption commit so only new commits accrue.

**After the first release ships, remove `"release-as": "0.1.0"` from
`release-please-config.json`** — otherwise release-please keeps proposing 0.1.0 forever.
From then on the version is derived purely from Conventional Commits.

Checklist for the first Release PR:
- [ ] `Directory.Build.props` `<Version>` and `version.txt` both show the new version.
- [ ] `CHANGELOG.md` reads sensibly.
- [ ] eSigner secrets are set on the `release` environment (else the run fails at the
      publish gate) — do a `publish=false` signing smoke test of `release.yml` first.
- [ ] After publish: `vX.Y.Z` tag exists, the Release has the zip + `SHA256SUMS.txt` +
      `Alarm.cdx.json`, `Alarm.exe` is Authenticode-signed, and `gh attestation verify` passes.
- [ ] `release-as` removed in a follow-up PR.

## Not yet included

- **ARM64 / MSIX.** Releases ship the self-contained **win-x64** unpackaged build only.

(Code signing **is** wired up — SSL.com eSigner in the `sign` job; see
[`docs/SIGNING.md`](SIGNING.md).)
