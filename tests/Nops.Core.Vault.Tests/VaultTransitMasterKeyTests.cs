using System.Net;
using System.Text;
using System.Text.Json;
using Nops.Core.Keys;

namespace Nops.Core.Vault.Tests;

public class VaultTransitMasterKeyTests
{
    private sealed class MockVault(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A Vault transit engine that "encrypted" the data key as <c>vault:v1:&lt;base64&gt;</c>.</summary>
    private static MockVault TransitMock() => new((req, body) =>
    {
        var ciphertext = JsonDocument.Parse(body).RootElement.GetProperty("ciphertext").GetString()!;
        var plaintext = ciphertext["vault:v1:".Length..];
        return Json("{\"data\":{\"plaintext\":\"" + plaintext + "\"}}");
    });

    [Fact]
    public async Task DecryptShare_PostsCiphertextToTransitDecryptAndReadsPlaintext()
    {
        var handler = TransitMock();
        var key = new VaultTransitMasterKey("https://vault.example.com:8200/", "transit", "my key",
            token: "s.token", httpClient: new HttpClient(handler));

        var data = await key.DecryptShareAsync("vault:v1:" + Convert.ToBase64String([1, 2, 3, 4]));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, data);
        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://vault.example.com:8200/v1/transit/decrypt/my%20key", request.RequestUri!.AbsoluteUri);
        Assert.Equal("s.token", Assert.Single(request.Headers.GetValues("X-Vault-Token")));
        Assert.Contains("vault:v1:AQIDBA==", body);
    }

    [Fact]
    public async Task DecryptShare_DoesNotModifyCallerOwnedHttpClient()
    {
        var client = new HttpClient(TransitMock());
        client.DefaultRequestHeaders.Add("X-Custom", "keep-me");
        var key = new VaultTransitMasterKey("https://vault", "transit", "k", token: "s.token", httpClient: client);

        await key.DecryptShareAsync("vault:v1:AQID");

        Assert.Equal("keep-me", Assert.Single(client.DefaultRequestHeaders.GetValues("X-Custom")));
        Assert.False(client.DefaultRequestHeaders.Contains("X-Vault-Token"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"errors":["permission denied"]}""")]
    [InlineData(HttpStatusCode.OK, """{"data":{}}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task DecryptShare_FailureReturnsNull(HttpStatusCode status, string body)
    {
        var key = new VaultTransitMasterKey("https://vault", "transit", "k",
            httpClient: new HttpClient(new MockVault((_, _) => Json(body, status))));

        Assert.Null(await key.DecryptShareAsync("vault:v1:AQID"));
    }

    [Fact]
    public async Task DecryptsFileWrittenByRealSops()
    {
        // vault-transit.sops.yaml: written by real sops against a mock transit engine (see Fixtures/README.md).
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var file = await File.ReadAllBytesAsync(Path.Combine(dir, "vault-transit.sops.yaml"));
        var expected = await File.ReadAllTextAsync(Path.Combine(dir, "vault-transit.sops.yaml.plain"));

        var key = new VaultTransitMasterKey("http://127.0.0.1:18200", "transit", "mykey",
            httpClient: new HttpClient(TransitMock()));
        var values = await SopsFile.DecryptToDictionaryAsync(file, "yaml",
            new SopsOptions { MasterKeys = [key], UseSopsEnvironment = false });

        Assert.Equal("1", values["a"]);
        Assert.Equal("secret", values["b"]);
        Assert.Contains("b: secret", expected);
    }
}

public class VaultTransitMasterKeyDefaultsTests
{
    [Fact]
    public void Properties_ReflectTheConstructorArguments()
    {
        var key = new VaultTransitMasterKey("https://vault", "transit", "k", "share", "s.token");

        Assert.Equal("hc_vault", key.KeyType);
        Assert.Equal("https://vault", key.Address);
        Assert.Equal("transit", key.MountPath);
        Assert.Equal("k", key.KeyName);
        Assert.Equal("share", key.EncryptedShare);
        Assert.Equal("s.token", key.Token);
        Assert.Null(key.HttpClient);
    }

    [Fact]
    public async Task UnreachableVault_ReturnsNull_UsingTheDefaultClient()
    {
        var key = new VaultTransitMasterKey("http://127.0.0.1:1", "transit", "k", token: "s.token");

        Assert.Null(await key.DecryptShareAsync("vault:v1:AQID"));
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var handler = new CancellingHandler();
        var key = new VaultTransitMasterKey("https://vault", "transit", "k", httpClient: new HttpClient(handler));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => key.DecryptShareAsync("vault:v1:AQID", cts.Token));
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage());
        }
    }
}
