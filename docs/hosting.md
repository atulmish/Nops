# Hosting guide

`AddSops` works on any `IConfigurationBuilder`, so it works wherever .NET configuration does. This page shows the usual hosts. Every example reads `secrets.sops.yaml` and assumes the key is provided as described in [Deploying with Nops](deployment.md#providing-the-key).

## Console applications and worker services

```csharp
using Microsoft.Extensions.Hosting;
using Nops.Configuration;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddSops("secrets.sops.yaml")
    .AddEnvironmentVariables()    // environment variables still override the file
    .AddCommandLine(args);

builder.Services.AddHostedService<Worker>();
builder.Build().Run();
```

The runnable version is [`samples/LayeredConfigSample`](https://github.com/atulmish/Nops/tree/main/samples/LayeredConfigSample). A worker service started by a service manager may not have the application folder as its working directory, so use `Path.Combine(AppContext.BaseDirectory, "secrets.sops.yaml")`.

## ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddSops("secrets.sops.yaml", opts => opts.WatchForChanges = true);

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateOnStart();
```

Use `IOptionsMonitor<T>` to observe reloads. The runnable version is [`samples/MinimalApiSample`](https://github.com/atulmish/Nops/tree/main/samples/MinimalApiSample).

## Azure Functions (isolated worker model)

Function apps usually run without a writable key file, so the recommended setup is an Azure Key Vault key that the function's managed identity can use. No key material is stored in the app.

```csharp
using Microsoft.Azure.Functions.Worker.Builder;
using Nops.Configuration;
using Nops.Core.Keys;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Configuration.AddSops(
    Path.Combine(AppContext.BaseDirectory, "secrets.sops.yaml"),
    opts =>
    {
        opts.UseSopsEnvironment = false;
        opts.MasterKeys = [new AzkvMasterKey("https://my-vault.vault.azure.net", "sops-wrapping-key")];
    });

builder.Build().Run();
```

`AzkvMasterKey` uses `DefaultAzureCredential`, which picks up the managed identity in Azure. Grant that identity the `unwrapKey` permission on the key. Install `Nops.Core.Azkv` for the provider. The same approach works for AWS Lambda with `Nops.Core.AwsKms`.

If you prefer an age key, store it in an application setting and set `SOPS_AGE_KEY`. Application settings are visible to anyone with access to the app configuration, so a KMS key is the stronger choice.

## Windows services and IIS

Set `SOPS_AGE_KEY_FILE` for the service account, or place the key at `%AppData%\sops\age\keys.txt` for that account, which is where the `sops` CLI looks. Restrict the file with an access control list so only the service account can read it.

## Which key delivery should I use?

| Host | Recommended key delivery |
|---|---|
| Kubernetes | A mounted Secret and `SOPS_AGE_KEY_FILE`, or a cloud KMS with workload identity |
| Azure App Service, Functions, Container Apps | Azure Key Vault key with a managed identity |
| AWS ECS, EKS, Lambda | AWS KMS key with the task or function role |
| A virtual machine or Windows service | A key file readable only by the service account |
| Local development | Your own age key at the default `sops` location |
