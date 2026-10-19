# Changelog

All notable changes to Nops are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Age keys are found the same way as the sops CLI: `SOPS_AGE_KEY`, `SOPS_AGE_KEY_FILE` and the default `sops/age/keys.txt` (opt out with `UseSopsEnvironment = false`)
- Decryption failures name the key types the file is encrypted for and where keys can be supplied
- NuGet packages include their README; KMS packages ship their own

### Changed

- `Nops.Configuration` depends on `Microsoft.Extensions.Configuration` and `Microsoft.Extensions.Hosting.Abstractions` instead of the ASP.NET Core shared framework, so it works in console apps and worker services
- The `encryptedShare` constructor argument of the KMS master keys is optional (it was never used; the share is read from the file)
- `PgpKeys` and `AgeIdentities` are used alongside `MasterKeys` in `Nops.Configuration`

### Fixed

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

[Unreleased]: https://github.com/atulmish/Nops/compare/v1.0.0-preview.1...HEAD
[1.0.0-preview.1]: https://github.com/atulmish/Nops/releases/tag/v1.0.0-preview.1
