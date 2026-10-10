# Quickstart - Nops

In-process SOPS decryption for .NET. Zero native dependencies.

## Installation

```bash
dotnet add package Nops.Core
```

For ASP.NET Core integration:

```bash
dotnet add package Nops.Configuration
```

For cloud KMS providers (optional):

```bash
dotnet add package Nops.Core.AwsKms     # AWS KMS
dotnet add package Nops.Core.GcpKms     # Google Cloud KMS
dotnet add package Nops.Core.Azkv        # Azure Key Vault
dotnet add package Nops.Core.Vault       # HashiCorp Vault + HCKMS
```

## Minimal Example - Decrypt a SOPS File

```csharp
using Nops.Core;

var fileBytes = File.ReadAllBytes("secrets.sops.yaml");

var options = new SopsOptions
{
    FilePath = "secrets.sops.yaml",
    AgeIdentities = new[] { /* age private keys or paths */ },
    // Or: PgpKeys = new[] { (pgpPrivateKeyBytes, passphrase) }
};

// Option 1: Get flattened key-value pairs
var dict = await SopsFile.DecryptToDictionaryAsync(fileBytes, "yaml", options);
Console.WriteLine(dict["app:db:password"]);

// Option 2: Get typed object
var config = await SopsFile.DeserializeAsync<MyConfig>(fileBytes, "yaml", options);
Console.WriteLine(config.Database.Password);

// Option 3: Get ordered tree (for round-trip edit + re-encrypt later)
var tree = await SopsFile.DecryptTreeAsync(fileBytes, "yaml", options);
```

## ASP.NET Core Example

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    opts.Optional = false;        // Throw if missing
    opts.WatchForChanges = true;  // Auto-reload on file change
});

var app = builder.Build();

app.MapGet("/health", () => 
    $"DB password loaded: {app.Configuration["app:db:password"]?.Substring(0, 2)}**");

app.Run();
```

## Running the Samples

```bash
# Minimal API sample
cd samples/MinimalApiSample
dotnet run

# Configuration sample
cd samples/ConfigurationSample  
dotnet run
```

Both samples use test fixtures - never prod keys.

## Next Steps

- [Configuration Provider](configuration.md) - ASP.NET Core integration details
- [Cloud KMS Setup](cloud-kms.md) - AWS, GCP, Azure, Vault authentication
- [Test Keys](test-keys.md) - Managing test-only cryptographic material
