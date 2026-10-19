# Quickstart

This guide takes you from a plaintext YAML file to typed, validated options in a .NET app.

## 1. Encrypt a file with sops

Nops reads files produced by the [sops](https://github.com/getsops/sops) CLI; it does not encrypt.

```bash
# One-time: create an age key where sops (and Nops) look for it
mkdir -p ~/.config/sops/age
age-keygen -o ~/.config/sops/age/keys.txt

# Encrypt
cat > secrets.yaml <<'EOF'
Database:
  Host: db.internal
  Password: s3cret
EOF
sops -e --age "$(age-keygen -y ~/.config/sops/age/keys.txt)" secrets.yaml > secrets.sops.yaml
rm secrets.yaml
```

For a team, add a [`.sops.yaml`](https://getsops.io/docs/) with `creation_rules` so `sops -e` and `sops edit` pick the recipients for you (see [`samples/.sops.yaml`](../samples/.sops.yaml)).

## 2. Load it as configuration

```bash
dotnet add package Nops.Configuration --prerelease
```

```csharp
using Nops.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddSops("secrets.sops.yaml");

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateDataAnnotations()   // Microsoft.Extensions.Options.DataAnnotations
    .ValidateOnStart();

var app = builder.Build();
app.Run();

public sealed class DatabaseOptions
{
    [Required] public string Host { get; set; } = "";
    [Required] public string Password { get; set; } = "";
}
```

No key configuration is needed on a machine where `sops decrypt` already works: Nops uses `SOPS_AGE_KEY`, `SOPS_AGE_KEY_FILE` and `~/.config/sops/age/keys.txt` like the CLI. In production, mount the key as a secret and set `SOPS_AGE_KEY_FILE`, or pass keys explicitly:

```csharp
builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    opts.AgeIdentities = ["/run/secrets/age-key.txt"];
    opts.UseSopsEnvironment = false;   // only use the keys listed here
});
```

## 3. Or decrypt without configuration

```bash
dotnet add package Nops.Core --prerelease
```

```csharp
using Nops.Core;

var bytes = await File.ReadAllBytesAsync("secrets.sops.yaml");
var options = new SopsOptions();

// Flattened key-value pairs ("Database:Password")
var values = await SopsFile.DecryptToDictionaryAsync(bytes, "yaml", options);

// Typed object (System.Text.Json binding over the decrypted tree)
var settings = await SopsFile.DecryptAsync<MySettings>(bytes, "yaml", options);

// Ordered tree, including comments, for tooling
var tree = await SopsFile.DecryptTreeAsync(bytes, "yaml", options);
```

The file kind is one of `yaml`, `json`, `dotenv`, `ini` or `binary`.

## Run the samples

Both samples ship a `secrets.sops.yaml` encrypted with the repository's TEST-ONLY age key, and their `launchSettings.json` points `SOPS_AGE_KEY_FILE` at it:

```bash
cd samples/ConfigurationSample && dotnet run      # generic host, validated options
cd samples/MinimalApiSample && dotnet run         # then: curl http://localhost:5080/
```

## Next steps

- [Configuration reference](configuration.md): all options, key flattening, reload, error handling
- [Cloud KMS setup](cloud-kms.md): AWS, GCP, Azure Key Vault, HashiCorp Vault
- [Test keys](test-keys.md): how fixtures and test keys are managed
