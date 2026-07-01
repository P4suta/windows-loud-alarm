# Code signing

Release binaries are Authenticode-signed in CI with an **SSL.com eSigner** cloud
certificate, via the official `SSLcom/esigner-codesign` Action (`command: batch_sign`).
Signing runs in the `sign` job of `.github/workflows/release.yml`, between `build` and
`publish`. A real publish **re-verifies** the bundle (chain + RFC 3161 timestamp +
expected signer subject) and **fails before creating the immutable Release** if it isn't
signed — so a missing or misconfigured secret can never ship an unsigned release.

## What gets signed

Only our own first-party PEs in `publish/dist/Alarm` — six of them. The root `Alarm.exe`
and `app/Alarm.exe` share a basename, so the job maps each bundle-relative path to a
unique staging name before `batch_sign`, then copies the signed file back:

- `Alarm.exe` (the root Native AOT launcher)
- `app/Alarm.exe` (apphost)
- `app/Alarm.dll` (Presentation)
- `app/Alarm.Application.dll`, `app/Alarm.Domain.dll`, `app/Alarm.Infrastructure.dll`

Bundled .NET / WindowsAppSDK / third-party DLLs are already vendor-signed and are left
alone — re-signing would waste eSigner quota and claim authorship we don't have.

## One-time setup (secrets only you can add)

Add these as **secrets on the `release` environment** (Settings → Environments →
`release` → Environment secrets). The `sign` job reads them.

| Secret | What it is |
|---|---|
| `ES_USERNAME` | SSL.com eSigner account username |
| `ES_PASSWORD` | SSL.com eSigner account password |
| `CREDENTIAL_ID` | The signing credential ID for your certificate |
| `ES_TOTP_SECRET` | The TOTP/OAuth secret for headless signing |

Find `CREDENTIAL_ID` and the TOTP secret in your SSL.com dashboard under the eSigner
credential (the TOTP secret is the base32 string behind the authenticator QR code).

Until these are set, the `sign` job passes the bundle through **unsigned with a
`::warning::`**, and a real (`publish=true`) release then **fails at the publish gate**. A
`publish=false` `workflow_dispatch` run of `release.yml` is a safe smoke test: it
builds + signs + verifies but creates no Release.

## The signer-subject assertion

`release.yml` sets `SIGNER_SUBJECT_CONTAINS` (workflow-level env) to a substring the
signing certificate's subject must contain — currently `CN=Yasunobu Sakashita`. The verify
step rejects any signature whose subject doesn't match, so even a validly-chained *wrong*
certificate can't pass. **If your ES certificate's subject differs, update that env value.**

## Verifying a downloaded release

```pwsh
# Authenticode (Windows):
Get-AuthenticodeSignature .\Alarm.exe | Format-List Status, SignerCertificate

# Build provenance (any OS with gh):
gh attestation verify Alarm-vX.Y.Z-win-x64.zip --repo P4suta/windows-loud-alarm
```
