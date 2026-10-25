# Migrating to Nops

Nops reads the files `sops` already wrote. Migrating means changing how the application reads them. The encrypted files do not change.

## From `sops -d` in CI or an entrypoint script

Before: the pipeline or entrypoint decrypts to a plaintext file, and the application reads that file.

```bash
sops -d secrets.sops.yaml > /app/secrets.yaml && dotnet MyApp.dll
```

After:

1. Add the package: `dotnet add package Nops.Configuration`.
2. Replace the file-based source with the encrypted file:

   ```csharp
   builder.Configuration.AddSops("secrets.sops.yaml");
   ```

3. Ship the encrypted file with the application, and provide the key as described in [Deploying with Nops](deployment.md#providing-the-key).
4. Remove the decrypt step, the `sops` binary from the image, and any plaintext file or Kubernetes Secret that existed only to carry the decrypted values.

Keys keep their names, so option classes and `IConfiguration` paths do not change. The `Database.Password` entry in the YAML is still `Database:Password`.

## From `dotnet user-secrets` or `dotnet-sops`

User-secrets are a development-only store. Keep using them locally if you like. For anything shared or deployed, move the values into a SOPS file:

```bash
sops -e --age "$(age-keygen -y ~/.config/sops/age/keys.txt)" secrets.yaml > secrets.sops.yaml
```

Then load the file with `AddSops` in all environments. See the [quickstart](quickstart.md).

## From environment variables carrying secrets

If secrets reach the application as environment variables (for example from Kubernetes Secrets), you can migrate one section at a time. `AddSops` followed by `AddEnvironmentVariables` means an existing variable still overrides the file, so you can move values into the SOPS file and delete the variables gradually:

```csharp
builder.Configuration
    .AddSops("secrets.sops.yaml")
    .AddEnvironmentVariables();
```

## From .NET wrappers around the sops command

Replace the wrapper call with `AddSops`. The files are the same. If the wrapper passed key material through environment variables or temporary files, switch to `SOPS_AGE_KEY_FILE` or a cloud KMS.

## Checking the result

Start the application with the key available and read a value. If the key is missing or wrong, the failure names the key types the file is encrypted for. A file that was edited without `sops edit` fails its MAC check with a `MacMismatchException`. See the troubleshooting table in [Deploying with Nops](deployment.md#troubleshooting).
