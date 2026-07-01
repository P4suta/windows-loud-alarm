# Security Policy

## Reporting a vulnerability

Please report security issues privately via GitHub's
[**Report a vulnerability**](https://github.com/P4suta/windows-loud-alarm/security/advisories/new)
form (Security → Advisories). Do **not** open a public issue for anything
security-sensitive.

You'll get an acknowledgement as soon as possible. Once a fix ships, the release
notes credit the reporter unless anonymity is requested.

## Supply-chain posture

- Releases are built in CI from a pinned toolchain (`mise.toml`) and carry keyless
  [Sigstore build-provenance and SBOM attestations](../docs/RELEASING.md) — verify a
  download with `gh attestation verify <zip> --repo P4suta/windows-loud-alarm`.
- Every GitHub Action is pinned to a full commit SHA; workflows default to a
  read-only `GITHUB_TOKEN` and grant writes only per-job.
- **Release binaries are Authenticode-signed** with an SSL.com eSigner certificate, and
  a real publish is hard-gated on a valid signature (chain + timestamp + expected
  signer). See [`docs/SIGNING.md`](../docs/SIGNING.md). Also verify `SHA256SUMS.txt`.
