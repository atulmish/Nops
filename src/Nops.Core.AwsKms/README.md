# Nops.Core.AwsKms

AWS KMS master key provider for [Nops](https://github.com/atulmish/Nops) - in-process SOPS decryption.

## Installation

```bash
dotnet add package Nops.Core.AwsKms
```

## Usage

```csharp
using Nops.Core;
using Nops.Core.Keys;

// Option 1: Default credential chain (environment variables, IMDS, shared config)
var masterKey = new AwsKmsMasterKey(
    arn: "arn:aws:kms:us-east-1:123456789012:key/abcd-1234-5678-efgh",
    context: new List<KeyValuePair<string, string>>
    {
        new KeyValuePair<string, string>("Project", "myapp")
    }
);

// Option 2: Explicit client (useful for LocalStack or custom endpoints)
var client = new AmazonKMSClient(
    new BasicAWSCredentials("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"),
    RegionEndpoint.USEast1
);

var masterKey2 = new AwsKmsMasterKey(
    arn: "arn:aws:kms:us-east-1:123456789012:key/abcd-1234-5678-efgh",
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

AWS KMS uses the [AWS SDK default credential chain](https://docs.aws.amazon.com/sdk-for-net/v2/developer-guide/creds-init.html):

1. **Environment variables**: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`
2. **Shared credentials file**: `~/.aws/credentials`
3. **IAM role** (EC2, ECS, EKS, Lambda)
4. **Amazon Cognito identity**

### LocalStack Development

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
```

## IAM Permissions

Minimum IAM policy for KMS decrypt:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "kms:Decrypt",
      "Resource": "arn:aws:kms:<region>:<account>:key/<key-id>"
    }
  ]
}
```

If using encryption context, the same policy applies - context matching is enforced at decrypt time.

## Encryption Context Mapping

SOPS stores encryption context as key-value pairs in `.sops.kms[].context`. These must match exactly what was used during encryption:

```yaml
sops:
  kms:
    - arn: arn:aws:kms:us-east-1:123456789012:key/abcd-1234
      enc: BASE64_ENCODED_SHARE
      context:
        Project: myapp
        Environment: production
```

When creating `AwsKmsMasterKey`, pass the same context:

```csharp
var key = new AwsKmsMasterKey(
    arn: "arn:aws:kms:us-east-1:123456789012:key/abcd-1234",
    context: new[] { 
        new KeyValuePair<string, string>("Project", "myapp"),
        new KeyValuePair<string, string>("Environment", "production") 
    }
);
```

## Running Tests

Unit tests run without dependencies:

```bash
dotnet test tests/Nops.Core.AwsKms.Tests --filter FullyQualifiedName~AwsKmsEntryParserTests
```

Integration tests need a KMS endpoint (for example LocalStack, started as shown above):

```bash
AWS_LIVE_TEST=1 dotnet test tests/Nops.Core.AwsKms.Tests --filter FullyQualifiedName~AwsKmsIntegrationTests
```

## License

MIT - see [LICENSE](https://github.com/atulmish/Nops/blob/main/LICENSE).
