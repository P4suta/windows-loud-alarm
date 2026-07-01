# Code signing

Release binaries are Authenticode-signed in CI with an **SSL.com eSigner** cloud
certificate, via the official `SSLcom/esigner-codesign` Action (`command: batch_sign`).
Signing happens in the `sign` job of `.github/workflows/release.yml`, between `build`
and `publish`, and is hard-gated: a real publish **re-verifies** the bundle is signed
(chain + RFC 3161 timestamp + expected signer subject) and **fails before creating the
immutable Release** if it isn't — so a missing/misconfigured secret can never ship an
unsigned release unnoticed.

## What gets signed

Only our own first-party PEs in the self-contained bundle (unique basenames, flat in
`publish/win-x64`):

- `Alarm.exe` (apphost)
- `Alarm.dll` (Presentation)
- `Alarm.Application.dll`, `Alarm.Domain.dll`, `Alarm.Infrastructure.dll`

The bundled .NET / WindowsAppSDK / third-party runtime DLLs are already
Microsoft-/vendor-signed and are deliberately left alone (re-signing would waste
eSigner quota and claim authorship we don't have).

## One-time setup (secrets only you can add)

Add these as **secrets on the `release` environment** (Settings → Environments →
`release` → Environment secrets). The `sign` job reads them; they never leave that
approval-gated job.

| Secret | What it is |
|---|---|
| `ES_USERNAME` | SSL.com eSigner account username |
| `ES_PASSWORD` | SSL.com eSigner account password |
| `CREDENTIAL_ID` | The signing credential ID for your certificate |
| `ES_TOTP_SECRET` | The TOTP/OAuth secret for automated (headless) signing |

Find the `CREDENTIAL_ID` and TOTP secret in your SSL.com dashboard under the eSigner
credential (the TOTP secret is the base32 string behind the authenticator QR code —
eSigner exposes it for CI use).

Until these are set, the `sign` job runs green and passes the bundle through
**unsigned with a `::warning::`**, and a real (`publish=true`) release then **fails at
the publish gate** rather than shipping unsigned. A `publish=false`
`workflow_dispatch` run of `release.yml` is a safe signing smoke test: it
builds + signs + verifies but creates no Release.

## The signer-subject assertion

`release.yml` sets `SIGNER_SUBJECT_CONTAINS` (workflow-level env) to the substring the
signing certificate's subject must contain — currently `CN=Yasunobu Sakashita`. The
verify step rejects a signature whose signer subject doesn't match, so even a validly-
chained but *wrong* certificate can't pass silently. **If your ES certificate's subject
differs, update that env value** (e.g. to your `CN=` / `O=`).

## Verifying a downloaded release

```pwsh
# Authenticode (Windows):
Get-AuthenticodeSignature .\Alarm.exe | Format-List Status, SignerCertificate

# Build provenance (any OS with gh):
gh attestation verify Alarm-vX.Y.Z-win-x64.zip --repo P4suta/windows-loud-alarm
```
