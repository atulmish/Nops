# Nops.Core.Azkv

Azure Key Vault master key provider for [Nops](https://github.com/getsops/nops) - in-process SOPS decryption.

## Installation

```bash
dotnet add package Nops.Core.Azkv
```

## Usage

```csharp
using Nops.Core;
using Nops.Core.Keys;

// Option 1: DefaultAzureCredential (env vars, managed identity, VS/Azure CLI auth)
var masterKey = new AzkvMasterKey(
    vaultUrl: "https://my-vault.vault.azure.net",
    keyName: "sops-wrapping-key",
    encryptedShare: "BASE64_ENCODED_ENCRYPTED_DATA_KEY"
);

// Option 2: Explicit client with specific credentials
var client = new KeyClient(
    new Uri("https://my-vault.vault.azure.net"),
    new Azure.Identity.ChainedTokenCredential(
        new Azure.Identity.EnvironmentCredential(),
        new Azure.AzureCliCredential()
    )
);

var masterKey2 = new AzkvMasterKey(
    vaultUrl: "https://my-vault.vault.azure.net",
    keyName: "sops-wrapping-key",
    encryptedShare: "BASE64_ENCODED_ENCRYPTED_DATA_KEY",
    client: client
);

// Use with SopsFile
var result = await SopsFile.DecryptTreeAsync(
    fileBytes: File.ReadAllBytes("secrets.sops.yaml"),
    fileKind: "yaml",
    options: new SopsOptions
    {
        MasterKeys = new[] { masterKey }
    });
```

## Authentication

Azure Key Vault uses [DefaultAzureCredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential):

1. **Environment variables**: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`
2. **Azure CLI**: `az login` (interactive)
3. **Visual Studio/Azure Developer CLI**: `dotnet azure login`
4. **Managed Identity**: Automatic when running on Azure (VM, App Service, Functions, etc.)

### Local Development

```bash
# Login with Azure CLI
az login

# Or set environment variables
export AZURE_TENANT_ID="your-tenant-id"
export AZURE_CLIENT_ID="your-client-id"
export AZURE_CLIENT_SECRET="your-client-secret"
```

## Key Vault Setup

Create a wrapping key for SOPS:

```bash
# Create Key Vault
az keyvault create --name my-vault --location eastus --resource-group my-rg

# Create a software-protected wrapping key
az keyvault key create \
    --name sops-wrapping-key \
    --vault-name my-vault \
    --protection software \
    --key-type RSA-HSM

# Grant your identity decrypt permissions
az keyvault set-policy \
    --name my-vault \
    --upn you@example.com \
    --key-permissions unwrapKey wrapKey
```

## Running Tests

Unit tests run without dependencies:

```bash
dotnet test tests/Nops.Core.Azkv.Tests --filter FullyQualifiedName~AzkvEntryParserTests
```

Integration tests require Azure credentials:

```bash
AZURE_LIVE_TEST=1 dotnet test tests/Nops.Core.Azkv.Tests --filter FullyQualifiedName~AzkvIntegrationTests
```

## License

MIT - see [LICENSE](../../LICENSE).
