# Cloud KMS Setup - Nops

Configuration for AWS KMS, Google Cloud KMS, Azure Key Vault, and HashiCorp Vault Transit.

## Overview

Nops supports decrypting data key shares encrypted under cloud KMS keys. Each provider ships as a separate NuGet package:

| Provider | Package | Key Type |
|---|---|---|
| AWS KMS | `Nops.Core.AwsKms` | `aws_kms` |
| Google Cloud KMS | `Nops.Core.GcpKms` | `gcp_kms` |
| Azure Key Vault | `Nops.Core.Azkv` | `azure_kv` |
| HashiCorp Vault Transit | `Nops.Core.Vault` | `hc_vault` |

> **Not supported:** sops' `hckms` key type is Huawei Cloud KMS. Files encrypted only for it cannot be decrypted by Nops.

### Using a provider

Create one master key per KMS key that can decrypt the file and pass them in
`MasterKeys`. You don't copy anything out of the file: the encrypted data key is
read from the file's `sops` metadata.

```csharp
builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    opts.MasterKeys = new IMasterKeyProvider[]
    {
        new AwsKmsMasterKey("arn:aws:kms:us-east-1:123456789012:key/abcd-1234"),
    };
});
```

`AgeIdentities`, `PgpKeys` and the sops environment keys are still tried
alongside `MasterKeys`.

## AWS KMS

### Installation

```bash
dotnet add package Nops.Core.AwsKms
```

### Authentication

Uses [AWS SDK default credential chain](https://docs.aws.amazon.com/sdk-for-net/v2/developer-guide/creds-init.html):

1. Environment variables: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`
2. Shared credentials file: `~/.aws/credentials`
3. IAM role (EC2, ECS, EKS, Lambda)

### Usage

```csharp
using Nops.Core.Keys;

var masterKey = new AwsKmsMasterKey(
    arn: "arn:aws:kms:us-east-1:123456789012:key/abcd-1234"
);
```

### IAM Policy

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Action": "kms:Decrypt",
    "Resource": "arn:aws:kms:<region>:<account>:key/<key-id>"
  }]
}
```

### Encryption Context

If the SOPS file was encrypted with encryption context, pass it when creating the master key:

```csharp
var masterKey = new AwsKmsMasterKey(
    arn: "arn:aws:kms:us-east-1:123456789012:key/abcd-1234",
    context: new[] { 
        new KeyValuePair<string, string>("Project", "myapp"),
        new KeyValuePair<string, string>("Env", "prod") 
    }
);
```

### LocalStack (Development)

```bash
docker run -p 4566:4566 localstack/localstack
```

```csharp
var client = new AmazonKMSClient(
    new BasicAWSCredentials("test", "test"),
    RegionEndpoint.USEast1)
{
    Config = new AmazonKMSConfig { ServiceURL = "http://localhost:4566" }
};

var masterKey = new AwsKmsMasterKey(
    arn: "arn:aws:kms:us-east-1:000000000000:key/test",
    client: client
);
```

## Google Cloud KMS

### Installation

```bash
dotnet add package Nops.Core.GcpKms
```

### Authentication

Uses [Application Default Credentials (ADC)](https://cloud.google.com/docs/authentication/application-default-credentials):

1. `gcloud auth application-default login`
2. Set `GOOGLE_APPLICATION_CREDENTIALS` to service account JSON key path
3. Automatic on GCE/GKE/GAE

### Usage

```csharp
using Nops.Core.Keys;

var masterKey = new GcpKmsMasterKey(
    resourceName: "projects/my-project/locations/global/keyRings/my-kr/cryptoKeys/my-key"
);
```

### IAM Role

```text
roles/cloudkms.cryptoKeyDecrypter
```

Or custom policy:

```json
{
  "permissions": [
    "cloudkms.cryptoKeys.get",
    "cloudkms.cryptoKeys.decrypt"
  ]
}
```

## Azure Key Vault

### Installation

```bash
dotnet add package Nops.Core.Azkv
```

### Authentication

Uses [DefaultAzureCredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential):

1. Environment variables: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`
2. `az login` (Azure CLI)
3. Managed Identity (on Azure)

### Usage

```csharp
using Nops.Core.Keys;

var masterKey = new AzkvMasterKey(
    vaultUrl: "https://my-vault.vault.azure.net",
    keyName: "sops-wrapping-key"
);
```

### Key Vault Setup

```bash
az keyvault create --name my-vault --location eastus --resource-group my-rg
az keyvault key create --name sops-wrapping-key --vault-name my-vault --protection software
az keyvault set-policy --name my-vault --upn you@example.com --key-permissions unwrapKey wrapKey
```

## HashiCorp Vault Transit

### Installation

```bash
dotnet add package Nops.Core.Vault
```

### Authentication

Pass a Vault token directly:

```csharp
using Nops.Core.Keys;

var masterKey = new VaultTransitMasterKey(
    address: "https://vault.example.com:8200",
    mountPath: "transit",
    keyName: "my-transit-key",
    token: "s.vault-token-here"
);
```

### Local Development

```bash
vault dev
# Token printed in output: s.abcd1234
export VAULT_TOKEN=s.abcd1234
export VAULT_ADDR=http://127.0.0.1:8200

vault secrets enable transit
vault write -f transit/keys/sops-key
```

## Multi-Provider Support

Nops tries all master keys until one succeeds. Mix providers in a single file:

```yaml
# secrets.sops.yaml
sops:
  age:
    - recipient: age1...
      enc: age-encryption.org/v1...
  kms:
    - arn: arn:aws:kms:...
      enc: BASE64...
  gcp_kms:
    - resource_name: projects/.../cryptoKeys/...
      enc: BASE64...
```

Nops goes through the file's key entries in order and asks each matching
provider to decrypt; the first success wins. With several key groups (Shamir),
it stops as soon as `shamir_threshold` groups have been decrypted.

## Testing

Each provider has integration tests gated by environment variables:

```bash
# AWS KMS (requires LocalStack)
AWS_LIVE_TEST=1 dotnet test --filter FullyQualifiedName~AwsKmsIntegrationTests

# GCP KMS (requires GCP credentials or emulator)
GCP_LIVE_TEST=1 dotnet test --filter FullyQualifiedName~GcpKmsIntegrationTests

# Azure Key Vault (requires Azure credentials)
AZURE_LIVE_TEST=1 dotnet test --filter FullyQualifiedName~AzkvIntegrationTests
```
