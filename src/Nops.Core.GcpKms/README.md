# Nops.Core.GcpKms

Google Cloud KMS master key provider for [Nops](https://github.com/atulmish/Nops) - in-process SOPS decryption.

## Installation

```bash
dotnet add package Nops.Core.GcpKms --prerelease
```

## Usage

```csharp
using Nops.Core;
using Nops.Core.Keys;

// Option 1: Application Default Credentials (ADC)
var masterKey = new GcpKmsMasterKey(
    resourceName: "projects/my-project/locations/global/keyRings/my-kr/cryptoKeys/my-key"
);

// Option 2: Explicit client with service account credentials
var client = KeyManagementServiceClient.Create(); // uses ADC by default

var masterKey2 = new GcpKmsMasterKey(
    resourceName: "projects/my-project/locations/global/keyRings/my-kr/cryptoKeys/my-key",
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

GCP KMS uses [Application Default Credentials (ADC)](https://cloud.google.com/docs/authentication/application-default-credentials):

1. **Google Cloud SDK**: `gcloud auth application-default login`
2. **Service account key**: Set `GOOGLE_APPLICATION_CREDENTIALS` to the path of a JSON key file
3. **GCE/GKE/GAE default service accounts**: Automatic when running on Google Cloud

### Local Development with Emulator

```bash
# Start a local KMS emulator (requires gcloud SDK)
gcloud kms emulators cloud-hsm --location=global --key-rings=my-kr --keys=my-key
export GCP_KMS_EMULATOR_HOST=localhost:5000
```

## IAM Permissions

Minimum IAM role for KMS decrypt:

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

## Running Tests

Unit tests run without dependencies:

```bash
dotnet test tests/Nops.Core.GcpKms.Tests --filter FullyQualifiedName~GcpKmsEntryParserTests
```

Integration tests require GCP credentials or emulator:

```bash
GCP_LIVE_TEST=1 dotnet test tests/Nops.Core.GcpKms.Tests --filter FullyQualifiedName~GcpKmsIntegrationTests
```

## License

MIT - see [LICENSE](../../LICENSE).
