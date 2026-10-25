<p align="center">
  <img src="https://raw.githubusercontent.com/atulmish/Nops/main/docs/images/banner.png" alt="Nops: read SOPS-encrypted secrets straight into .NET configuration" width="100%">
</p>

<p align="center">
  <a href="https://github.com/atulmish/Nops/actions/workflows/ci.yml"><img src="https://github.com/atulmish/Nops/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/atulmish/Nops/actions/workflows/codeql.yml"><img src="https://github.com/atulmish/Nops/actions/workflows/codeql.yml/badge.svg" alt="CodeQL"></a>
  <a href="https://securityscorecards.dev/viewer/?uri=github.com/atulmish/Nops"><img src="https://api.securityscorecards.dev/projects/github.com/atulmish/Nops/badge" alt="OpenSSF Scorecard"></a>
  <a href="https://atulmish.github.io/Nops/"><img src="https://github.com/atulmish/Nops/actions/workflows/pages.yml/badge.svg" alt="Docs"></a>
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Nops.Core"><img src="https://img.shields.io/nuget/v/Nops.Core.svg?label=Nops.Core&logo=nuget" alt="Nops.Core on NuGet"></a>
  <a href="https://www.nuget.org/packages/Nops.Configuration"><img src="https://img.shields.io/nuget/v/Nops.Configuration.svg?label=Nops.Configuration&logo=nuget" alt="Nops.Configuration on NuGet"></a>
  <a href="https://www.nuget.org/packages/Nops.Core"><img src="https://img.shields.io/nuget/dt/Nops.Core.svg?label=downloads&color=blue" alt="NuGet downloads"></a>
  <a href="https://dotnet.microsoft.com/download"><img src="https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4?logo=dotnet" alt=".NET 8 and .NET 10"></a>
  <a href="https://github.com/atulmish/Nops/actions/workflows/ci.yml"><img src="https://img.shields.io/badge/coverage-%E2%89%A595%25%20enforced-brightgreen" alt="Line coverage of at least 95% is enforced in CI"></a>
</p>

<p align="center">
  <a href="https://github.com/atulmish/Nops/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="License: MIT"></a>
  <a href="https://github.com/atulmish/Nops/releases"><img src="https://img.shields.io/github/v/release/atulmish/Nops?include_prereleases&logo=github" alt="Latest release"></a>
  <a href="https://github.com/atulmish/Nops/commits/main"><img src="https://img.shields.io/github/last-commit/atulmish/Nops?logo=git&logoColor=white" alt="Last commit"></a>
  <a href="https://github.com/atulmish/Nops/blob/main/CONTRIBUTING.md"><img src="https://img.shields.io/badge/PRs-welcome-brightgreen.svg" alt="PRs welcome"></a>
  <a href="https://github.com/atulmish/Nops/security/dependabot"><img src="https://img.shields.io/badge/dependabot-enabled-025E8C?logo=dependabot" alt="Dependabot enabled"></a>
  <a href="https://github.com/atulmish/Nops/stargazers"><img src="https://img.shields.io/github/stars/atulmish/Nops?style=flat&logo=github" alt="GitHub stars"></a>
</p>

<p align="center">
  <a href="https://atulmish.github.io/Nops/"><b>Documentation</b></a> &nbsp;·&nbsp;
  <a href="https://github.com/atulmish/Nops/blob/main/docs/quickstart.md">Quickstart</a> &nbsp;·&nbsp;
  <a href="https://github.com/atulmish/Nops/blob/main/samples/README.md">Samples</a> &nbsp;·&nbsp;
  <a href="https://github.com/atulmish/Nops/blob/main/CHANGELOG.md">Changelog</a>
</p>

**Read [SOPS](https://github.com/getsops/sops)-encrypted secrets straight into .NET configuration. No `sops` binary, no subprocess, no plaintext on disk.**

You keep encrypting and editing secrets with the `sops` CLI as usual and commit the encrypted files. Your app decrypts them in-process at startup:

```csharp
builder.Configuration.AddSops("secrets.sops.yaml");
```

Nops finds your age keys the same way `sops` does, decrypts the data key (age, PGP, AWS KMS, GCP KMS, Azure Key Vault, HashiCorp Vault), checks the file's MAC, and exposes the values through `IConfiguration`, ready for `IOptions<T>`.

<p align="center">
  <img src="https://raw.githubusercontent.com/atulmish/Nops/main/docs/images/flow.png" alt="An encrypted secrets.sops.yaml committed to git is decrypted in-process by Nops (find key, unwrap data key, verify MAC, decrypt in memory) into IConfiguration values" width="100%">
</p>

## The problem Nops solves

[SOPS](https://github.com/getsops/sops) is a good way to keep secrets in git: values are encrypted, keys stay readable, diffs are reviewable, and access is controlled with age, PGP or a cloud KMS. But SOPS is a Go command-line tool, and a .NET app cannot read a SOPS file by itself. In practice teams reach for one of these:

| What you do today | What it costs |
|---|---|
| Run `sops -d` in CI or an entrypoint script and ship the result | A plaintext copy of the secrets on disk, in the image layer or in a Kubernetes Secret. An extra pipeline step to keep working. |
| Bake the `sops` binary into the image and shell out at startup (including the .NET wrappers around the `sops` and `age` CLIs) | A native binary per OS and architecture to ship, pin and patch. A subprocess, a shell and temp files at startup. Awkward on distroless images, Windows and Azure Functions. |
| Cluster tooling such as helm-secrets, ksops or a SOPS operator | Kubernetes only. The secrets end up as plaintext Kubernetes Secrets, and nothing helps outside a cluster or on a laptop. |
| Move to a runtime secret store (Vault, Key Vault, Secrets Manager) | A network dependency and infrastructure to run, and secrets leave your repo, so you lose reviewable, versioned, encrypted-in-git config. |

**Nops is the missing piece for .NET: a library that reads the same SOPS files in-process.** There is no `sops` binary at runtime, no subprocess and no plaintext on disk. Your app opens the encrypted file with the key it already has, verifies the file's MAC, and exposes the values as ordinary `IConfiguration`, so `IOptions<T>`, layering, environment-variable overrides and reload on change all work as usual.

You keep everything good about SOPS: your team still edits secrets with the `sops` CLI, the files in git don't change, and access is still decided by age, PGP or KMS keys. Nops only replaces the "decrypt it somehow before the app starts" step.

**Use Nops if** you have a .NET app, you want secrets encrypted in git, and you would rather not ship the `sops` binary or run a decrypt step. **Look elsewhere if** you need to create or edit encrypted files from code (Nops only decrypts, use the `sops` CLI), you are not on .NET, or you already run a secret store and are happy with it.

## Why Nops

<p align="center">
  <img src="https://raw.githubusercontent.com/atulmish/Nops/main/docs/images/features.png" alt="In-process, tamper-evident, same files as sops, fits .NET configuration, pay for what you use, reloads on change" width="100%">
</p>

<details>
<summary>The same, as text</summary>

- **No extra tooling at runtime.** Your container doesn't need the `sops` binary, a shell or a temporary plaintext file.
- **Same files as the `sops` CLI.** Every test fixture is generated by real `sops` (3.13) and checked against `sops decrypt` in CI.
- **Tamper-evident.** The file's MAC is verified before any value is returned.
- **Fits .NET configuration.** Values are layered, overridable, reloadable and bindable like any other configuration source.
- **Pay for what you use.** `Nops.Core` has no cloud SDK dependencies; each KMS lives in its own package.

</details>

## Install

```bash
dotnet add package Nops.Configuration   # IConfiguration provider (includes Nops.Core)
dotnet add package Nops.Core            # Decryption API only
```

| Package | Use it for |
|---|---|
| [`Nops.Core`](https://www.nuget.org/packages/Nops.Core) | Decrypting files directly; age and PGP keys |
| [`Nops.Configuration`](https://www.nuget.org/packages/Nops.Configuration) | `IConfiguration` provider with reload on change (ASP.NET Core, generic host, console) |
| [`Nops.Core.AwsKms`](https://www.nuget.org/packages/Nops.Core.AwsKms) | AWS KMS keys |
| [`Nops.Core.GcpKms`](https://www.nuget.org/packages/Nops.Core.GcpKms) | Google Cloud KMS keys |
| [`Nops.Core.Azkv`](https://www.nuget.org/packages/Nops.Core.Azkv) | Azure Key Vault keys |
| [`Nops.Core.Vault`](https://www.nuget.org/packages/Nops.Core.Vault) | HashiCorp Vault Transit keys |

Targets .NET 8 and .NET 10, the two long-term support releases. Applications on .NET 9 use the .NET 8 build.

## Quickstart

Encrypt a file with `sops` as usual:

```bash
age-keygen -o ~/.config/sops/age/keys.txt          # once per machine
sops -e --age "$(age-keygen -y ~/.config/sops/age/keys.txt)" secrets.yaml > secrets.sops.yaml
```

### ASP.NET Core / generic host

```csharp
using Nops.Configuration;

var builder = WebApplication.CreateBuilder(args);   // or Host.CreateApplicationBuilder(args)

builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    opts.WatchForChanges = true;   // IOptionsMonitor<T> sees rotated secrets
});

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateOnStart();
```

`Database.Password` in the YAML becomes `Database:Password` in `IConfiguration`, the same as with `appsettings.json`.

### Decrypt directly

```csharp
using Nops.Core;

var bytes = await File.ReadAllBytesAsync("secrets.sops.yaml");

// Flattened "a:b:c" keys
IReadOnlyDictionary<string, string> values =
    await SopsFile.DecryptToDictionaryAsync(bytes, "yaml", new SopsOptions());

// Or bind to a type
var settings = await SopsFile.DecryptAsync<MySettings>(bytes, "yaml", new SopsOptions());
```

## Plain config plus one secrets file

Most apps keep `appsettings.json` in plaintext and put only the secrets in a SOPS file. Sources layer in the order you add them, so the SOPS values override the plaintext file and environment variables still win:

```csharp
builder.Configuration
    .AddSops("secrets.sops.yaml")
    .AddEnvironmentVariables()
    .AddCommandLine(args);
```

Config diffs stay readable in review and only the secrets are encrypted. See [Deploying with Nops](https://github.com/atulmish/Nops/blob/main/docs/deployment.md) for Kubernetes and Docker setups, partial encryption with `encrypted_regex`, and key rotation.

## Where keys come from

| Source | How |
|---|---|
| sops environment (default) | `SOPS_AGE_KEY`, `SOPS_AGE_KEY_FILE`, then `$XDG_CONFIG_HOME/sops/age/keys.txt` (`~/.config/…`, `~/Library/Application Support/…` or `%AppData%\…`). Turn off with `UseSopsEnvironment = false`. |
| Explicit age keys | `opts.AgeIdentities = ["AGE-SECRET-KEY-1…", "/run/secrets/age.txt"]` (keys or key-file paths) |
| PGP | `opts.PgpKeys = [(File.ReadAllBytes("key.asc"), passphrase)]` (armored or binary secret key) |
| Cloud KMS / Vault | `opts.MasterKeys = [new AwsKmsMasterKey("arn:aws:kms:…")]`. See [Cloud KMS setup](https://github.com/atulmish/Nops/blob/main/docs/cloud-kms.md). |

In containers, mount the age key as a secret and point `SOPS_AGE_KEY_FILE` at it. Files with several key groups (Shamir `shamir_threshold`) work as soon as enough of the keys are available.

## Security model

- The file's MAC is verified before any value is returned, so a tampered or truncated file fails instead of loading partly.
- Decrypted values live in memory only. Nops never writes plaintext to disk and starts no subprocess.
- Key material is read from the same places as the `sops` CLI, or from a cloud KMS so no key is on disk at all.
- `Optional = true` tolerates a **missing** file only. A present file that fails to decrypt or fails its MAC check always throws, so a required secret never silently disappears.

To report a vulnerability, see [SECURITY.md](https://github.com/atulmish/Nops/blob/main/SECURITY.md).

## Supported

<p align="center">
  <img src="https://raw.githubusercontent.com/atulmish/Nops/main/docs/images/keys.png" alt="Formats: YAML, JSON, dotenv, INI, binary. Keys: age, PGP, AWS KMS, GCP KMS, Azure Key Vault, HashiCorp Vault Transit" width="100%">
</p>

| | |
|---|---|
| Formats | YAML, JSON, dotenv, INI, binary |
| Master keys | age (X25519), PGP, AWS KMS, GCP KMS, Azure Key Vault, HashiCorp Vault Transit |
| sops features | MAC verification, key groups + Shamir threshold, `unencrypted_suffix` / `encrypted_suffix`, `unencrypted_regex` / `encrypted_regex`, comment regexes, `mac_only_encrypted`, YAML anchors and comments |
| Configuration | Reload on change, optional files, multiple files layered in order, max file size |

### Not supported (yet)

- **Encrypting or editing.** Nops only decrypts; use the `sops` CLI to create and change files.
- `SOPS_AGE_KEY_CMD`, age SSH identities, age plugins, passphrase (scrypt) and post-quantum age recipients.
- PGP keys from a gpg keyring or agent: pass the secret key bytes instead.

Relative file paths are resolved against the current working directory.

## Documentation

- [Quickstart](https://github.com/atulmish/Nops/blob/main/docs/quickstart.md): first decrypt, typed binding, samples
- [Configuration reference](https://github.com/atulmish/Nops/blob/main/docs/configuration.md): every option, key flattening, reload, errors
- [Deploying with Nops](https://github.com/atulmish/Nops/blob/main/docs/deployment.md): config layout, Kubernetes and Docker, key rotation, troubleshooting
- [Cloud KMS setup](https://github.com/atulmish/Nops/blob/main/docs/cloud-kms.md): AWS, GCP, Azure, Vault
- [Test keys](https://github.com/atulmish/Nops/blob/main/docs/test-keys.md): how the fixtures are generated
- [Samples](https://github.com/atulmish/Nops/blob/main/samples/README.md): console, minimal API and layered-config apps you can run

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](https://github.com/atulmish/Nops/blob/main/CONTRIBUTING.md). To report a vulnerability, follow [SECURITY.md](https://github.com/atulmish/Nops/blob/main/SECURITY.md) instead of opening an issue.

```bash
dotnet build Nops.slnx
dotnet test Nops.slnx
```

Cloud integration tests are skipped unless their environment is available (for example `AWS_LIVE_TEST=1` with LocalStack).

## License

MIT. See [LICENSE](https://github.com/atulmish/Nops/blob/main/LICENSE). Nops reimplements the SOPS file format and cryptography in C# without copying Go code. SOPS itself is MPL-2.0.
