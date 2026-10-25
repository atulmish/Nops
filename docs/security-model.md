# Security model

This page states what Nops guarantees, how the implementation was verified, and what it does not do. If you find a problem, report it privately as described in [SECURITY.md](https://github.com/atulmish/Nops/blob/main/SECURITY.md).

## What Nops protects

- **Secrets in git stay encrypted.** Nops reads the files the `sops` CLI writes and changes nothing about them.
- **Decrypted values live in memory only.** Nops writes no plaintext file, starts no subprocess and needs no `sops` binary.
- **Tampering is detected.** The file's MAC covers every value, including those stored in plaintext, and is verified before any value is returned. An edited, truncated or corrupt file raises `MacMismatchException`.
- **Failures are not silent.** A wrong key, a malformed age blob and a failed MAC check each raise an error. `Optional = true` tolerates only a missing file.
- **Key material is handled carefully.** Age private keys and decrypted data keys are wiped from the buffers Nops controls once they are no longer needed. Secret comparisons use constant-time routines.

## What Nops does not protect

- **The running process.** Anyone who can read the memory or environment of the process, or who can call into it, can read the decrypted values. SOPS does not change this.
- **The key.** Whoever holds an age key or KMS permission that is a recipient of the file can decrypt it. Protect keys as you would any credential, and prefer a cloud KMS with workload identity where you can.
- **Secrets you put in plaintext.** With `encrypted_regex`, only matching keys are encrypted. A secret under an unexpected name stays in plaintext. Prefer `unencrypted_regex` when in doubt.
- **Secrets after they leave configuration.** Values bound to options objects and logged or serialised later are outside Nops's control.

## How the implementation was verified

| Check | What it covers |
|---|---|
| Real `sops` output | Every fixture is written by `sops` 3.13 and decrypted by `sops` in CI, and by Nops in the test suite. |
| C2SP age test vectors | The 101 vectors that use an X25519 identity: every file that must be accepted is, and every file that must be rejected is. They cover armor, stanza parsing, the header MAC and the STREAM payload. |
| Cross-check against the `age` tool | Files produced by `age` (empty, 64 KiB, 128 KiB and larger payloads, armored and binary, several recipients) decrypt byte for byte. Flipped, truncated, extended and reordered inputs are rejected. |
| Hostile input | Large and highly repetitive headers are rejected in linear time. A randomised mutation test and property-based tests (FsCheck) run in CI. They corrupt valid files and feed random text and bytes to the age, YAML, JSON, dotenv and INI parsers, and assert that only the documented exception types ever escape. |
| Test coverage | More than 95% of lines are covered, and CI fails below that. |
| Static analysis | CodeQL runs on every change and weekly, and dependencies are reviewed on pull requests. |

The age implementation supports X25519 recipients only, which is what `sops` uses to wrap its data key. Passphrase (scrypt), post-quantum and plugin recipients are not supported and fail with a clear error.

## Supply chain

- Packages are published by a GitHub Actions workflow using NuGet trusted publishing (short-lived credentials, no stored API key) from a version tag.
- Builds are deterministic in CI and include source link and symbol packages, so a binary can be matched to its source.
- A software bill of materials (CycloneDX) is attached to each GitHub release.
- From the first release made after version 1.1.0, each package has a signed [build provenance attestation](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations) and a Sigstore signature bundle (`*.nupkg.sigstore.json`) attached to the GitHub release. See "Verifying a release" below.
- Workflows run with read-only tokens by default and grant write access only to the job that needs it. Every action is pinned to a commit SHA, and Dependabot proposes updates.
- Dependabot and dependency review keep dependencies current. Direct dependencies are limited to BouncyCastle, YamlDotNet and the Microsoft.Extensions packages, plus the cloud SDKs in the optional KMS packages.
- The main branch requires a pull request and passing checks, and release tags cannot be moved or deleted.

## Known limits

- There has been no independent third-party security audit. The age and MAC code is small and heavily tested, and reviews are welcome. Please open an issue or a private advisory.
- Nops only decrypts. It cannot create or edit encrypted files.
- Configuration values are strings in memory, as with any .NET configuration source, and are not zeroed after use.

## Verifying a release

For a package published after version 1.1.0, download the `.nupkg` from the GitHub release (or from NuGet) and check that it was built by this repository's release workflow:

```bash
gh attestation verify Nops.Core.<version>.nupkg --repo atulmish/Nops

cosign verify-blob Nops.Core.<version>.nupkg \
  --bundle Nops.Core.<version>.nupkg.sigstore.json \
  --certificate-identity-regexp 'https://github.com/atulmish/Nops/.github/workflows/release.yml@refs/tags/v.*' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

Both commands fail if the file was not produced by that workflow from a version tag.
