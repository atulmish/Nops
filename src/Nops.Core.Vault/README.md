# Nops.Core.Vault

HashiCorp Vault Transit master key provider for [Nops](https://github.com/atulmish/Nops) - in-process SOPS decryption.

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
    token: "s.vault-token-here"
);

// Option 2: With custom HttpClient (e.g., for mTLS)
var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("X-Vault-Token", "s.vault-token-here");

var masterKey2 = new VaultTransitMasterKey(
    address: "https://vault.example.com:8200",
    mountPath: "transit",
    keyName: "my-transit-key",
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
vault write -f transit/keys/sops-wrapping-key

# Grant permissions to your identity (decrypting needs "update" on the decrypt path)
vault policy write sops-policy - <<EOF
path "transit/decrypt/sops-wrapping-key" {
  capabilities = ["update"]
}
EOF
```

## Running Tests

The unit tests need no Vault server. They use a mock `HttpClient`, plus a file written by real `sops` against a mock transit engine (see `tests/Nops.Core.Vault.Tests/Fixtures/README.md`):

```bash
dotnet test tests/Nops.Core.Vault.Tests
```

## License

MIT - see [LICENSE](https://github.com/atulmish/Nops/blob/main/LICENSE).

> sops' `hckms` key type is **Huawei Cloud KMS**, which Nops does not support.
