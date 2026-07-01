# Releasing

Releases are automated from [Conventional Commits](https://www.conventionalcommits.org/)
by [release-please](https://github.com/googleapis/release-please). You never hand-pick or
hand-edit a version.

Design rationale is in
[`docs/adr/0001-automated-versioning-with-release-please.md`](adr/0001-automated-versioning-with-release-please.md).

## How a release happens

1. **Commits land on `main`.** `feat:` → minor, `fix:`/`perf:` → patch, `!` or
   `BREAKING CHANGE:` → major. PRs squash-merge, so the **PR title** is what release-please
   parses (enforced by the `pr-title` check).
2. **release-please keeps a Release PR open** (`.github/workflows/release-please.yml`),
   bumping the version in `Directory.Build.props` (`x-release-please-version` annotation)
   and `version.txt`, and regenerating `CHANGELOG.md`. Labelled `autorelease: pending`.
3. **A human approves and merges.** Add the `release: approved` label, then merge by hand.
4. **release-please cuts the tag + a DRAFT Release** (`draft: true`,
   `force-tag-creation: true`) and dispatches `release.yml`.
5. **`release.yml` builds → signs → publishes.** `build` runs `just publish` + CycloneDX
   SBOM. `sign` (manual approval in the `release` environment) Authenticode-signs our six
   first-party PEs with SSL.com eSigner. `publish` (a *second* `release` approval)
   re-verifies signatures (hard gate), runs `just package vX.Y.Z` (zip + `SHA256SUMS.txt`),
   writes keyless Sigstore provenance + SBOM attestations, attaches assets, and
   **publishes** the Release. Assets attach *before* publish (GitHub Immutable Releases
   require that order). See [`docs/SIGNING.md`](SIGNING.md).

Verify a downloaded asset:

```sh
gh attestation verify <zip> --repo P4suta/windows-loud-alarm
sha256sum -c SHA256SUMS.txt
# On Windows, after extracting: Get-AuthenticodeSignature Alarm.exe, app\Alarm.exe
```

## Safety gates

| Gate | Effect |
|---|---|
| `release-gate` (CI) | Release PR can't merge without the `release: approved` label. |
| `no-automerge-on-release-pr` | Turns auto-merge back off on Release PRs → release is always a deliberate manual merge. |
| `release-label-guard` | Restores `autorelease: pending` if removed from an open Release PR. |
| Dispatch-only `release.yml` | A stray `git push vX.Y.Z` starts nothing; only release-please dispatches it. |
| Two environments | `release-please` (unattended, `main`-only secrets); `release` (two human approvals — `sign` and `publish` — before the irreversible publish). |

## One-time activation: the GitHub App

Until the App secrets are set, `release-please.yml` runs green and **no-ops**. A GitHub
App token (not `GITHUB_TOKEN`) is required because only an App/PAT push can trigger the
downstream `release.yml`, and its API commits satisfy the signed-commits ruleset.

The `release-please` and `release` environments are **already provisioned** (see below).
Only the secrets remain, and only you can add those:

1. **Create a GitHub App** (org or personal): permissions **Contents: Read & write** and
   **Pull requests: Read & write**. No webhook.
2. **Install it** on the `windows-loud-alarm` repo.
3. **Generate a private key** (downloads a `.pem`).
4. **Add two secrets to the `release-please` environment** (Settings → Environments →
   `release-please` → Environment secrets):
   - `RELEASE_PLEASE_CLIENT_ID` — the App's **Client ID** (not the numeric App ID).
   - `RELEASE_PLEASE_PRIVATE_KEY` — the full `.pem` contents.
5. **Add the SSL.com eSigner secrets to the `release` environment** — `ES_USERNAME`,
   `ES_PASSWORD`, `CREDENTIAL_ID`, `ES_TOTP_SECRET`. See [`docs/SIGNING.md`](SIGNING.md).

> A fine-grained **PAT** with the same Contents + Pull-requests scopes is a drop-in
> fallback: use it as the source for both `RELEASE_PLEASE_*` secrets. The App is preferred
> (short-lived tokens, not tied to a person).

### Environments already provisioned

Idempotent — safe to re-run:

- **`release-please`** — branch policy **`main` only**, **no reviewers**.
- **`release`** — **required reviewer: you**; `release.yml`'s `sign` and `publish` jobs
  each stop for it (two approvals per release, by design).

## Apply the branch rulesets (one-time)

The rulesets live under `.github/rulesets/` but must be applied once (Settings → Rules →
Rulesets → *Import*, or via `gh api`):

```sh
gh api --method POST repos/P4suta/windows-loud-alarm/rulesets \
  --input .github/rulesets/protect-default-branch.json
gh api --method POST repos/P4suta/windows-loud-alarm/rulesets \
  --input .github/rulesets/require-signed-commits.json
```

> **`require-signed-commits` requires your local git to sign commits** (`git config
> commit.gpgsign true` + a signing key). release-please's own commits are GitHub-signed
> and satisfy it automatically. Skip that ruleset if you don't want to sign locally yet —
> the rest of the pipeline is unaffected.

## First release

The first release is pinned to **v0.1.0** via `"release-as": "0.1.0"` in
`release-please-config.json`, with the manifest seeded at `0.0.0` and `bootstrap-sha` at
the adoption commit.

**After the first release ships, remove `"release-as": "0.1.0"`** — otherwise
release-please keeps proposing 0.1.0 forever. From then on the version derives purely from
Conventional Commits.

Checklist for the first Release PR:
- [ ] `Directory.Build.props` `<Version>` and `version.txt` both show the new version.
- [ ] `CHANGELOG.md` reads sensibly.
- [ ] eSigner secrets are set on the `release` environment — do a `publish=false` signing
      smoke test of `release.yml` first.
- [ ] After publish: `vX.Y.Z` tag exists, the Release has the zip + `SHA256SUMS.txt` +
      `Alarm.cdx.json`, `Alarm.exe` is Authenticode-signed, and `gh attestation verify` passes.
- [ ] `release-as` removed in a follow-up PR.

## Not yet included

- **ARM64 / MSIX.** Releases ship the self-contained **win-x64** unpackaged build only.
