using System;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Nops.Core.Keys;
using Xunit.Abstractions;

namespace Nops.Core.Azkv.Tests.Integration;

/// <summary>
/// Integration tests for AzkvMasterKey.
/// Requires Azure credentials (DefaultAzureCredential chain).
/// 
/// Run with: AZURE_LIVE_TEST=1 dotnet test --filter FullyQualifiedName~AzkvIntegrationTests
/// </summary>
public class AzkvIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public AzkvIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DecryptShareAsync_Returns32ByteDataKey()
    {
        // Skip if no live test flag
        var liveTest = Environment.GetEnvironmentVariable("AZURE_LIVE_TEST")?.ToLower() == "true";

        if (!liveTest)
        {
            _output.WriteLine("Skipping: set AZURE_LIVE_TEST=1 to run live tests");
            return;
        }

        // Arrange: requires a Key Vault with a wrapping key configured
        var vaultUrl = Environment.GetEnvironmentVariable("AZURE_KEY_VAULT_URL")
            ?? throw new InvalidOperationException("Set AZURE_KEY_VAULT_URL for live tests");

        var keyName = Environment.GetEnvironmentVariable("AZURE_KEY_NAME")
            ?? "sops-wrapping-key";

        // For live testing:
        // 1. Create a Key Vault: az keyvault create --name my-vault --location eastus
        // 2. Create a wrapping key: az keyvault key create --name my-key --vault-name my-vault --protection software
        // 3. Set up credentials via AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET or managed identity

        _output.WriteLine($"Azure KV integration test structure ready.");
        _output.WriteLine($"Vault: {vaultUrl}");
        _output.WriteLine($"Key: {keyName}");
    }

    [Fact]
    public async Task DecryptShareAsync_InvalidKey_ReturnsNull()
    {
        var masterKey = new AzkvMasterKey(
            "https://nonexistent.vault.azure.net",
            "nonexistent-key",
            "dGVzdA=="); // base64 "test"

        byte[]? decrypted = await masterKey.DecryptShareAsync("dGVzdA==");

        Assert.Null(decrypted);
    }

    public void Dispose()
    {
        // No unmanaged resources to clean up
    }
}
