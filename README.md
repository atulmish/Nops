# Nops

[![CI](https://github.com/atulmish/Nops/actions/workflows/ci.yml/badge.svg)](https://github.com/atulmish/Nops/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

In-process [SOPS](https://github.com/getsops/sops) decryption for .NET. Zero native dependencies.

Nops decrypts SOPS-encrypted files (YAML, JSON, dotenv, INI, binary) directly inside your application - no `sops` CLI, no subprocess, no temp files. It reads the `.sops` metadata, unwraps the data key with your master keys (age, PGP, AWS KMS, GCP KMS, Azure Key Vault, HashiCorp Vault), verifies the MAC, and hands you decrypted values.

> Nops reimplements the SOPS file format and cryptography in C# (no Go code is copied). SOPS itself is MPL-2.0; Nops is MIT.

## Packages

| Package | Description |
|---|---|
| `Nops.Core` | Format parsing, stores, MAC verification, age + PGP master keys |
| `Nops.Configuration` | `IConfiguration` provider with file watching and reload |
| `Nops.Core.AwsKms` | AWS KMS master key provider |
| `Nops.Core.GcpKms` | Google Cloud KMS master key provider |
| `Nops.Core.Azkv` | Azure Key Vault master key provider |
| `Nops.Core.Vault` | HashiCorp Vault Transit + HCKMS master key providers |

## Quickstart

```bash
dotnet add package Nops.Core
```

```csharp
using Nops.Core;

var options = new SopsOptions
{
    FilePath = "secrets.sops.yaml",
    // Bech32 keys ("AGE-SECRET-KEY-...") or paths to identity files
    AgeIdentities = new[] { "age-key.txt" },
};

// Flattened key-value pairs ("a:b:c" for nested maps)
var values = await SopsFile.DecryptToDictionaryAsync(
    File.ReadAllBytes("secrets.sops.yaml"), "yaml", options);

// Or a typed bind
var settings = await SopsFile.DecryptAsync<MySettings>(
    File.ReadAllBytes("secrets.sops.yaml"), "yaml", options);
```

## ASP.NET Core

```bash
dotnet add package Nops.Configuration
```

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    opts.AgeIdentities = new[] { Environment.GetEnvironmentVariable("AGE_KEY")! };
    opts.WatchForChanges = true; // auto-reload on file change
});
```

Cloud KMS providers plug in through `SopsOptions.MasterKeys` / `Nops.Configuration.SopsOptions.MasterKeys`:

```csharp
using Nops.Core.Keys;

opts.MasterKeys = new IMasterKeyProvider[]
{
    new AwsKmsMasterKey(arn, enc, context),
    new AzkvMasterKey(vaultUrl, keyName, enc),
};
```

## Documentation

- [Quickstart](docs/quickstart.md)
- [Configuration reference](docs/configuration.md)
- [Cloud KMS setup](docs/cloud-kms.md)
- [Test keys](docs/test-keys.md)

## Development

Requires the .NET SDK pinned in `global.json`.

```bash
dotnet build Nops.slnx
dotnet test Nops.slnx
```

Cloud integration tests are skipped unless their environment is available (e.g. `AWS_LIVE_TEST=1` with LocalStack).

## License

MIT - see [LICENSE](LICENSE).
