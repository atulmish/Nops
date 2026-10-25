# Configuration Provider

`Microsoft.Extensions.Configuration` provider for SOPS-encrypted files. It works with
ASP.NET Core, the generic host (`Host.CreateApplicationBuilder`), worker services and
plain `ConfigurationBuilder`; it does not depend on the ASP.NET Core shared framework.

## Installation

```bash
dotnet add package Nops.Configuration
```

## Basic Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

// Keys come from the sops environment by default (see "Keys" below)
builder.Configuration.AddSops("secrets.sops.yaml");

builder.Services.AddOptions<DbOptions>().BindConfiguration("app:db");

var app = builder.Build();
var password = app.Configuration["app:db:password"];
```

Sources are layered in the order they are added, so add `AddSops` after
`appsettings.json` and before anything that should override it (environment variables
and the command line are already added by `WebApplication.CreateBuilder`, so call
`builder.Configuration.AddEnvironmentVariables()` again after `AddSops` if
environment variables must win).

Relative paths are resolved against the current working directory (not the content root, which `AddJsonFile` uses). For services whose
working directory is not the app folder, pass
`Path.Combine(AppContext.BaseDirectory, "secrets.sops.yaml")`.

## Keys

| Option | Description |
|---|---|
| `UseSopsEnvironment` (default `true`) | Use `SOPS_AGE_KEY`, `SOPS_AGE_KEY_FILE` and `$XDG_CONFIG_HOME/sops/age/keys.txt` (`~/.config`, `~/Library/Application Support` or `%AppData%`), like the sops CLI |
| `AgeIdentities` | `AGE-SECRET-KEY-...` strings or paths to age key files |
| `PgpKeys` | `(byte[] key, string? passphrase)` tuples, armored or binary secret keys |
| `MasterKeys` | Any `IMasterKeyProvider`, e.g. cloud KMS keys ([Cloud KMS setup](cloud-kms.md)) |

All configured sources are tried together; each distinct age key is tried once.

## Options

```csharp
builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    // File format (auto-detected from extension: .yaml/.yml, .json, .env/.dotenv, .ini, .bin)
    opts.FileKind = "yaml";  // yaml | json | dotenv | ini | binary

    // Keys (see the table above)
    opts.AgeIdentities = ["AGE-SECRET-KEY-...", "/path/to/age-keys.txt"];
    opts.PgpKeys = [(pgpKeyBytes, "passphrase")];
    opts.UseSopsEnvironment = true;
    
    // Watch for file changes and reload automatically
    opts.WatchForChanges = true;
    opts.ReloadInterval = TimeSpan.FromMilliseconds(250);  // Debounce
    
    // Handle missing files gracefully
    opts.Optional = false;           // Throw if the file is missing (default)
    
    // Maximum file size (default: 10MB)
    opts.MaxFileSize = 50 * 1024 * 1024;
});
```

## Key Flattening Rules

SOPS-encrypted files have nested structures. The configuration provider flattens them to `IConfiguration` key format:

### Nested Maps → Colon-Separated Keys

```yaml
app:
  db:
    host: localhost
    port: 5432
```

Becomes:

| Key | Value |
|---|---|
| `app:db:host` | `localhost` |
| `app:db:port` | `5432` |

### Arrays → Index in Key

```yaml
servers:
  - host: server1.example.com
    port: 8080
  - host: server2.example.com
    port: 8081
```

Becomes:

| Key | Value |
|---|---|
| `servers:0:host` | `server1.example.com` |
| `servers:0:port` | `8080` |
| `servers:1:host` | `server2.example.com` |
| `servers:1:port` | `8081` |

> **Note:** Array indices use the .NET `IConfiguration` convention (`list:0:key`), NOT SOPS AAD format. SOPS AAD does not include array indices.

### Special Value Types

| Type | Format in IConfiguration |
|---|---|
| `string` | Raw value |
| `int` / `float` | String representation (e.g., `"5432"`) |
| `bool` | `True` or `False` (Python-cased, matches SOPS canonical) |
| `bytes` | Base64-encoded string |
| `time` | ISO-8601 string (e.g., `2025-01-15T10:30:00Z`) |
| `Comment` | Skipped (not exposed) |

### INI and dotenv

INI sections become the first key segment (`[db]` + `user=` → `db:user`). Keys before
the first section are in the `DEFAULT` section (`DEFAULT:name`), as in sops. Dotenv
keys are used as-is.

### Case Insensitivity

Keys are case-insensitive by default (standard .NET `IConfiguration` behavior):

```csharp
var v1 = config["app:db:host"];    // "localhost"
var v2 = config["APP:DB:HOST"];    // "localhost" (same)
var v3 = config["App:Db:Host"];    // "localhost" (same)
```

## Multi-File Overlay

Add multiple SOPS files - last source wins for overlapping keys:

```csharp
// Base secrets (required)
builder.Configuration.AddSops("secrets.sops.yaml", opts => 
{
    opts.Optional = false;
});

// Environment overrides (optional)
builder.Configuration.AddSops($"secrets-{env}.sops.yaml", opts => 
{
    opts.Optional = true;  // A missing file contributes no values; any other failure still throws
});
```

## File Watching

Enable automatic reload when the SOPS file changes:

```csharp
opts.WatchForChanges = true;
opts.ReloadInterval = TimeSpan.FromMilliseconds(250);  // Debounce (default)
```

When the file changes:

1. The provider re-reads and decrypts the file
2. `OnReload` is triggered on the `IConfigurationRoot`
3. Any registered change tokens fire

> **Note:** The provider watches the file's directory, so it sees files rewritten in place, atomic replacements (write a temp file, rename it over the original) and Kubernetes Secret and ConfigMap volume updates, which swap a `..data` symlink instead of writing the file. Bursts of events collapse into one reload after `ReloadInterval`. On Linux, inotify limits may apply for large directories.
>
> If a reload fails (a bad key, a half-written or tampered file), the previous values stay in place and `OnReloadError` is called, so log there:
>
> ```csharp
> opts.OnReloadError = ex => logger.LogError(ex, "SOPS reload failed; keeping the previous values");
> ```

## Error Handling

| Scenario | Behavior |
|---|---|
| File not found | `FileNotFoundException`, or no values when `Optional = true` |
| No usable key | `InvalidOperationException` ("Cannot decrypt: none of the N configured master key(s)..."), naming the key types the file is encrypted for. The message ends with `Last error:` when a key failed with an error rather than simply not matching (a tampered age blob, an unreachable KMS) |
| MAC mismatch (file edited without sops, truncated) | `MacMismatchException` |
| PGP key without its passphrase | `NotSupportedException`, only if no other key could decrypt |
| File larger than `MaxFileSize` | `InvalidOperationException` |
| Unknown file extension | `InvalidOperationException`: set `FileKind` |

`Optional = true` covers **only a missing file**. A file that is present but cannot be decrypted (wrong key, failed MAC) always throws, so a required secret can never silently disappear.

## Host Builder Extension

For `IHostApplicationBuilder` (generic host):

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddSops("secrets.sops.yaml", opts =>
{
    opts.Optional = false;
});

var host = builder.Build();
```
