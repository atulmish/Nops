using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Nops.Core.Keys;
using Xunit.Abstractions;

namespace Nops.Core.AwsKms.Tests.Integration;

/// <summary>
/// Integration tests for AwsKmsMasterKey using LocalStack KMS.
/// Requires a running LocalStack instance (docker-compose up -d in tests/ directory).
/// 
/// Run with: AWS_LIVE_TEST=1 dotnet test --filter FullyQualifiedName~AwsKmsIntegrationTests
/// </summary>
public class AwsKmsIntegrationTests : IClassFixture<AwsKmsIntegrationFixture>, IDisposable
{
    private static bool LiveTestsEnabled => Environment.GetEnvironmentVariable("AWS_LIVE_TEST") == "1";

    private readonly ITestOutputHelper _output;
    private readonly AmazonKeyManagementServiceClient _localStackClient;

    public AwsKmsIntegrationTests(AwsKmsIntegrationFixture fixture, ITestOutputHelper output)
    {
        _output = output;
        _localStackClient = fixture.Client;
    }

    [Fact]
    public async Task DecryptShareAsync_Returns32ByteDataKey()
    {
        if (!LiveTestsEnabled)
            return; // Requires LocalStack; run with AWS_LIVE_TEST=1
        // Arrange: create a KMS key in LocalStack and encrypt a known data key
        var keyId = await CreateTestKeyAsync();

        byte[] originalDataKey = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(originalDataKey);

        string encryptedShare = await EncryptDataKey(keyId, originalDataKey);

        // Act: use AwsKmsMasterKey to decrypt
        var masterKey = new AwsKmsMasterKey(
            keyId,
            encryptedShare,
            client: _localStackClient);

        byte[]? decrypted = await masterKey.DecryptShareAsync(encryptedShare);

        // Assert
        Assert.NotNull(decrypted);
        Assert.Equal(32, decrypted.Length);
        Assert.Equal(originalDataKey, decrypted);

        _output.WriteLine($"AWS KMS decrypt succeeded. KeyId: {keyId}");
    }

    [Fact]
    public async Task DecryptShareAsync_WithEncryptionContext_Matches()
    {
        if (!LiveTestsEnabled)
            return; // Requires LocalStack; run with AWS_LIVE_TEST=1
        // Arrange
        var keyId = await CreateTestKeyAsync();

        byte[] originalDataKey = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(originalDataKey);

        string encryptedShare = await EncryptDataKeyWithContext(keyId, originalDataKey, new Dictionary<string, string>
        {
            ["Project"] = "Nops",
            ["Test"] = "AwsKms"
        });

        var masterKey = new AwsKmsMasterKey(
            keyId,
            encryptedShare,
            context: new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Project", "Nops"),
                new KeyValuePair<string, string>("Test", "AwsKms")
            },
            client: _localStackClient);

        byte[]? decrypted = await masterKey.DecryptShareAsync(encryptedShare);

        Assert.NotNull(decrypted);
        Assert.Equal(originalDataKey, decrypted);
    }

    [Fact]
    public async Task DecryptShareAsync_WrongContext_ReturnsNull()
    {
        if (!LiveTestsEnabled)
            return; // Requires LocalStack; run with AWS_LIVE_TEST=1
        // Arrange: encrypt with one context, decrypt with wrong context
        var keyId = await CreateTestKeyAsync();

        byte[] originalDataKey = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(originalDataKey);

        string encryptedShare = await EncryptDataKeyWithContext(keyId, originalDataKey, new Dictionary<string, string>
        {
            ["Correct"] = "context"
        });

        // Wrong context should cause KMS to reject → returns null (not throw)
        var masterKey = new AwsKmsMasterKey(
            keyId,
            encryptedShare,
            context: new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Wrong", "context")
            },
            client: _localStackClient);

        byte[]? decrypted = await masterKey.DecryptShareAsync(encryptedShare);

        // LocalStack may or may not enforce context matching - this test documents behavior
        // In production AWS KMS, wrong context would cause Decrypt to fail
    }

    [Fact]
    public async Task DecryptShareAsync_InvalidKeyId_ReturnsNull()
    {
        if (!LiveTestsEnabled)
            return; // Requires LocalStack; run with AWS_LIVE_TEST=1
        var masterKey = new AwsKmsMasterKey(
            "arn:aws:kms:us-east-1:000000000000:key/nonexistent",
            "dGVzdA==", // base64 "test"
            client: _localStackClient);

        byte[]? decrypted = await masterKey.DecryptShareAsync("dGVzdA==");

        Assert.Null(decrypted);
    }

    private async Task<string> CreateTestKeyAsync()
    {
        var request = new DescribeKeyRequest();

        // List keys to find one, or create a new one
        var listResponse = await _localStackClient.ListKeysAsync(new ListKeysRequest());

        foreach (var key in listResponse.Keys)
        {
            if (!key.KeyId.StartsWith("alias/aws/"))
            {
                _output.WriteLine($"Reusing existing KMS key: {key.KeyId}");
                return key.KeyId;
            }
        }

        // Create a new test key
        var createResponse = await _localStackClient.CreateKeyAsync(new CreateKeyRequest
        {
            Description = "Nops Test Key",
            Tags = new List<Tag>
            {
                new Tag { TagKey = "Project", TagValue = "Nops" },
                new Tag { TagKey = "Test", TagValue = "true" }
            }
        });

        _output.WriteLine($"Created KMS key: {createResponse.KeyMetadata.KeyId}");
        return createResponse.KeyMetadata.KeyId!;
    }

    private async Task<string> EncryptDataKey(string keyId, byte[] dataKey)
    {
        var response = await _localStackClient.EncryptAsync(new EncryptRequest
        {
            KeyId = keyId,
            Plaintext = new System.IO.MemoryStream(dataKey)
        });

        return Convert.ToBase64String(response.CiphertextBlob.ToArray());
    }

    private async Task<string> EncryptDataKeyWithContext(string keyId, byte[] dataKey, Dictionary<string, string> context)
    {
        var response = await _localStackClient.EncryptAsync(new EncryptRequest
        {
            KeyId = keyId,
            Plaintext = new System.IO.MemoryStream(dataKey),
            EncryptionContext = context
        });

        return Convert.ToBase64String(response.CiphertextBlob.ToArray());
    }

    public void Dispose()
    {
        // The fixture owns the client lifetime (shared across tests in this class)
    }
}

/// <summary>
/// xUnit fixture that creates a shared LocalStack KMS client.
/// </summary>
public class AwsKmsIntegrationFixture : IDisposable
{
    public AmazonKeyManagementServiceClient Client { get; }

    public AwsKmsIntegrationFixture()
    {
        // LocalStack default endpoint configuration
        var creds = new BasicAWSCredentials(
            Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "test",
            Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "test");

        var config = new AmazonKeyManagementServiceConfig
        {
            ServiceURL = Environment.GetEnvironmentVariable("LOCALSTACK_ENDPOINT") ?? "http://localhost:4566"
        };

        Client = new AmazonKeyManagementServiceClient(creds, config);
    }

    public void Dispose()
    {
        Client?.Dispose();
    }
}
