# Roadmap

This is the direction for Nops after 1.1. Items are not promises or dates. Their order reflects how often they are asked for and how much they widen who can use Nops. The [open issues labelled `good first issue`](https://github.com/atulmish/Nops/labels/good%20first%20issue) are the best place to start contributing, and [Discussions](https://github.com/atulmish/Nops/discussions) is the place to propose or vote for something else.

## Next

- **More age key sources.** Support `SOPS_AGE_KEY_CMD`, age SSH identities and passphrase (scrypt) recipients, so every way the `sops` CLI finds an age key also works in Nops.
- **Loading from a stream.** Read an encrypted file from an embedded resource, a `Stream` or a byte array, for hosts where the file is not on disk.
- **.NET Aspire and Azure Functions.** A tested sample for each, with the recommended key delivery.
- **Post-quantum age recipients.** Support the hybrid recipients introduced in recent age releases, once `sops` can produce them.

## Later

- Published benchmark results for decrypting small and large files.
- An analyzer or startup check that warns when a secret-looking key is stored unencrypted.
- A migration tool that converts `dotnet user-secrets` into a SOPS file.

## Not planned

- **Encrypting or editing files.** Nops only decrypts. Use the `sops` CLI to create and edit files.
- **Fetching secrets from a runtime store.** The Azure Key Vault, AWS Secrets Manager and Vault configuration providers already do this. Nops is for secrets that are versioned and reviewed in git.

## Compatibility promise

Nops follows [Semantic Versioning](https://semver.org/). The public API of the 1.x releases will not break. The libraries target .NET 8 and .NET 10, the long-term support releases, and a new release is added when it becomes generally available.
