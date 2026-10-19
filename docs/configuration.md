# Configuration Provider - Nops

ASP.NET Core `IConfiguration` integration for SOPS-encrypted files.

## Installation

```bash
dotnet add package Nops.Configuration
```

## Basic Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddSops("secrets.sops.yaml");

var app = builder.Build();

// Access secrets via standard IConfiguration
var password = app.Configuration["app:db:password"];
```

## Options

```csharp
builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    // Required: path to the SOPS-encrypted file
    opts.FilePath = "secrets.sops.yaml";
    
    // File format (auto-detected from extension if omitted)
    opts.FileKind = "yaml";  // yaml | json | dotenv | ini | binary
    
    // Age identities (private keys or file paths)
    opts.AgeIdentities = new[] { "AGE-SECRET-KEY-...", "/path/to/age-keys" };
    
    // PGP private keys
    opts.PgpKeys = new[] { (pgpKeyBytes, "passphrase") };
    
    // Watch for file changes and reload automatically
    opts.WatchForChanges = true;
    opts.ReloadInterval = TimeSpan.FromMilliseconds(250);  // Debounce
    
    // Handle missing files gracefully
    opts.Optional = false;           // Throw if missing (default)
    opts.FileNotFoundMode = FileNotFoundMode.WarnAndContinue;
    
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
    opts.Optional = true;  // Won't fail if file missing
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

> **Note:** File watching uses `FileSystemWatcher`. On Linux, inotify limits may apply for large directories.

## Error Handling

| Scenario | Default Behavior | With `Optional = true` |
|---|---|---|
| File not found | Throw `FileNotFoundException` | Empty config (no values) |
| MAC mismatch | Throw `MacMismatchException` | Empty config (no values) |
| Decryption failure | Throw `InvalidOperationException` / `MacMismatchException` | Empty config (no values) |
| File too large | Throw `InvalidOperationException` | N/A |

Customize with `FileNotFoundMode`:

```csharp
opts.FileNotFoundMode = FileNotFoundMode.WarnAndContinue;  // Log warning, continue
opts.FileNotFoundMode = FileNotFoundMode.Ignore;           // Silent ignore
```

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
