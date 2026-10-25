# Changelog

All notable changes to Nops are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-10-25

### Added

- Every library now targets `net8.0` and `net10.0` (it was `net10.0` only), so applications on .NET 8 and .NET 9 can use Nops. `Nops.Configuration` depends on the matching `Microsoft.Extensions.*` major version for each target (8.x on .NET 8, 10.x on .NET 10)

### Changed

- The test suite runs on both target frameworks in CI

## [1.0.0] - 2026-10-25

First stable release.

### Added

- Age decryption is implemented in `Nops.Core` itself and checked against the C2SP age test vectors (101 X25519 vectors vendored under `tests/Nops.Core.Tests/Fixtures/age-cctv`)
- Age keys are found the same way as the sops CLI: `SOPS_AGE_KEY`, `SOPS_AGE_KEY_FILE` and the default `sops/age/keys.txt` (opt out with `UseSopsEnvironment = false`)
- Decryption failures name the key types the file is encrypted for and where keys can be supplied
- NuGet packages include their README; KMS packages ship their own

### Changed

- `Optional = true` now covers only a missing file. Previously it also swallowed a wrong key or a failed MAC check, so a tampered optional file silently produced an empty configuration
- File watching now watches the file's directory and debounces properly. It sees atomic replacements and Kubernetes Secret/ConfigMap volume updates (symlink swap), which it missed before. A failed reload keeps the previous values and calls the new `OnReloadError` option
- `SopsConfigurationProvider` implements `IDisposable`, so disposing the configuration root stops the file watcher
- `IHostApplicationBuilder.AddSops` returns the builder; its unused `name` parameter is removed. The extensions class is renamed `ConfigurationBuilderExtensions` (call sites are unchanged)
- `MacMismatchException`, `SopsMetadataException` and the tree and metadata types (`SopsTree`, `SopsMetadata`, `TreeBranch`, ...) live in the `Nops.Core` namespace
- `MacMismatchException` no longer includes the stored and computed MACs in its message
- A malformed or tampered age blob is reported (`Last error:` in the "Cannot decrypt" message) instead of looking like a wrong key
- `AgeMasterKey` takes just the private key; the unused `recipient` argument is removed
- Age decryption wipes the private key and decrypted data key from memory it controls
- The public API is narrowed to what the documentation uses. Stores, tree walking, MAC verification, Shamir, AES-GCM, key-resolution and the KMS entry parsers are now internal
- `Nops.Configuration.SopsOptions` is renamed `SopsConfigurationOptions`, so it no longer clashes with `Nops.Core.SopsOptions` when both namespaces are imported
- `Nops.Core` no longer depends on the prerelease `AgeSharp` package, so every Nops package has only stable dependencies
- `Nops.Configuration` depends on `Microsoft.Extensions.Configuration` and `Microsoft.Extensions.Hosting.Abstractions` instead of the ASP.NET Core shared framework, so it works in console apps and worker services
- The `encryptedShare` constructor argument of the KMS master keys is optional (it was never used; the share is read from the file)
- `PgpKeys` and `AgeIdentities` are used alongside `MasterKeys` in `Nops.Configuration`

### Removed

- `FileNotFoundMode` and `SopsConfigurationOptions.FileNotFoundMode`: `Ignore` was never honoured, and the other modes only added to what `Optional` already did
- `GcpKmsMasterKey`'s `context` argument: sops' GCP KMS keys have no encryption context, so it could only break decryption
- The unused `AWSSDK.S3` dependency of `Nops.Core.AwsKms`
- `HckmsMasterKey` and the `hckms` claims. sops' `hckms` key type is Huawei Cloud KMS, not HashiCorp Cloud KMS, and the provider modelled a service that does not exist
- `VaultTransitMasterKey`'s `version` argument: the key version is part of the ciphertext and Vault's decrypt endpoint takes none

### Fixed

- Binary (non-armored) PGP secret keys could not be loaded: the armor reader closed the stream the fallback needed. `gpg --export-secret-keys` output without `--armor` now works
- `-0.0` is encoded as `-0` (it was `0`), matching sops, so a file containing negative zero no longer fails its MAC check
- `DecryptAsync<T>` binds property names case-insensitively, like `IConfiguration` (a `ratio` key now fills `Ratio`)
- Azure Key Vault unwrapping now uses the credential of a caller-supplied `KeyClient`
- The metadata entry parsers read the field names sops actually writes (`vault_address`/`engine_path`/`key_name`, `resource_id`, `vaultUrl`)
- Vault Transit decryption never worked: the response's `data.plaintext` was read as `data.ciphertext`. It is now checked end to end against a file written by real `sops`, and the token is sent per request instead of rewriting a shared `HttpClient`'s default headers
- PGP: decrypt files encrypted to a key's encryption subkey (the normal gpg setup) and unwrap the compressed/literal packets around the data key
- A PGP key with a missing passphrase no longer prevents other master keys from being tried
- YAML: aliases expand to the anchored content; block-end comments attach to the right key; comments in sequences no longer shift element indices
- INI: keys before the first section are kept under `DEFAULT`; values are no longer newline-unescaped
- JSON: integers beyond 2^53 or the int64 range no longer cause a MAC mismatch
- Dotenv: multiple age/PGP recipients are all written; `lastmodified` keeps its UTC offset
- Configuration: reloads replace data atomically, so readers never see a partial set of keys; age identities are no longer tried twice
- `DecryptAsync<T>` accepts YAML `.nan` / `.inf` values

## [1.0.0-preview.1] - 2026-10-10

Initial preview release.

### Added

- `Nops.Core`: in-process SOPS decryption for YAML, JSON, dotenv, INI, and binary formats, with MAC verification and Shamir secret sharing
- Master key providers for age and PGP
- `Nops.Configuration`: ASP.NET Core `IConfiguration` provider with file watching and reload
- `Nops.Core.AwsKms`: AWS KMS master key provider
- `Nops.Core.GcpKms`: Google Cloud KMS master key provider
- `Nops.Core.Azkv`: Azure Key Vault master key provider
- `Nops.Core.Vault`: HashiCorp Vault Transit and HCKMS master key providers
- Samples for minimal API and configuration usage
- Documentation: quickstart, configuration reference, cloud KMS setup, test keys

[Unreleased]: https://github.com/atulmish/Nops/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/atulmish/Nops/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/atulmish/Nops/compare/v1.0.0-preview.1...v1.0.0
[1.0.0-preview.1]: https://github.com/atulmish/Nops/releases/tag/v1.0.0-preview.1
