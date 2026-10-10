# Changelog

All notable changes to Nops are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
