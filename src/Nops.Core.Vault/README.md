# Nops.Core.Vault

HashiCorp Vault Transit and HCKMS master key providers for [Nops](https://github.com/getsops/nops) - in-process SOPS decryption.

## Installation

```bash
dotnet add package Nops.Core.Vault
```

## Usage

### Vault Transit Secrets Engine

```csharp
using Nops.Core;
using Nops.Core.Keys;

// Option 1: With Vault token
var masterKey = new VaultTransitMasterKey(
    address: "https://vault.example.com:8200",
    mountPath: "transit",
    keyName: "my-transit-key",
    encryptedShare: "BASE64_ENCODED_ENCRYPTED_DATA_KEY",
    token: "s.vault-token-here"
);

// Option 2: With custom HttpClient (e.g., for mTLS)
var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("X-Vault-Token", "s.vault-token-here");

var masterKey2 = new VaultTransitMasterKey(
    address: "https://vault.example.com:8200",
    mountPath: "transit",
    keyName: "my-transit-key",
    encryptedShare: "BASE64_ENCODED_ENCRYPTED_DATA_KEY",
    httpClient: httpClient
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

### HashiCorp Cloud KMS (HCKMS)

```csharp
// HCKMS is accessed via Vault Transit endpoint that proxies to GCP KMS
var hckmsKey = new HckmsMasterKey(
    projectId: "my-gcp-project",
    location: "global",
    keyRing: "my-keyring",
    cryptoKey: "my-hckms-key",
    encryptedShare: "BASE64_ENCODED_ENCRYPTED_DATA_KEY",
    vaultToken: "s.vault-token-here"  // required for Vault-mediated access
);
```

## Authentication

### Local Development with `vault dev`

```bash
# Start local Vault server
vault dev

# The token will be printed in the output, e.g.:
# Token: s.abcd1234efgh5678
export VAULT_TOKEN="s.abcd1234efgh5678"
export VAULT_ADDR="http://127.0.0.1:8200"

# Create a transit key
vault secrets enable transit
vault write -f transit/keys/sops-key
```

### Production Authentication

- **Token**: Pass `X-Vault-Token` header directly
- **AppRole**: Use `vault login -method=approle` to get a token
- **Kubernetes**: Use Kubernetes auth method
- **TLS/mTLS**: Configure client certificates on the HttpClient

## Vault Transit Setup

```bash
# Enable transit secrets engine (if not already enabled)
vault secrets enable transit

# Create a transit key
vault write -f transit/keys/sops-wrapping-key \
    type=rsa-4096 \
    convergent-encryption=true

# Grant permissions to your identity
vault policy write sops-policy <<EOF
path "transit/data/sops-wrapping-key" {
  capabilities = ["update"]
}
EOF
```

## Running Tests

Unit tests run without dependencies:

```bash
dotnet test tests/Nops.Core.Vault.Tests --filter FullyQualifiedName~VaultEntryParserTests
```

Integration tests require a running Vault server:

```bash
docker run -p 8200:8200 -e VAULT_DEV_ROOT_TOKEN_ID=root -e VAULT_DEV_LISTEN_ADDRESS=0.0.0.0:8200 vault:latest dev
VAULT_ADDR=http://localhost:8200 dotnet test tests/Nops.Core.Vault.Tests --filter FullyQualifiedName~VaultIntegrationTests
```

## License

MIT - see [LICENSE](../../LICENSE).
