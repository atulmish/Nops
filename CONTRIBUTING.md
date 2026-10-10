# Contributing to Nops

Thanks for your interest in improving Nops!

## Development setup

1. Install the .NET SDK version pinned in `global.json`.
2. Clone the repository and build:

   ```bash
   dotnet build Nops.slnx
   dotnet test Nops.slnx
   ```

3. Before opening a pull request, make sure the format check passes:

   ```bash
   dotnet format --verify-no-changes
   ```

## Project layout

- `src/Nops.Core` - format parsing, stores, MAC verification, age and PGP master keys. This project must not reference any cloud SDK.
- `src/Nops.Core.AwsKms`, `src/Nops.Core.GcpKms`, `src/Nops.Core.Azkv`, `src/Nops.Core.Vault` - cloud master key providers.
- `src/Nops.Configuration` - ASP.NET Core `IConfiguration` integration.
- `tests/` - xUnit test projects, one per source project.
- `docs/` - user documentation.
- `samples/` - runnable examples.

## Pull requests

- Add or update tests for behavior changes.
- Keep public API changes minimal and documented with XML doc comments.
- Cloud integration tests are skipped unless their environment is available (for example `AWS_LIVE_TEST=1` with LocalStack). Unit tests must pass without any cloud access.
- Describe what changed and why in the pull request body.

## Reporting bugs

Please use the GitHub issue tracker. Include the Nops package version, the file format (YAML, JSON, dotenv, INI, binary), and a minimal reproduction. Redact any real secrets from files you attach.

## Security issues

Do not open a public issue for security problems. See [SECURITY.md](SECURITY.md).
