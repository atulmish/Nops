using Google.Cloud.Kms.V1;
using Google.Protobuf;
using Grpc.Core;
using Nops.Core.Keys;

namespace Nops.Core.GcpKms.Tests;

/// <summary>Decrypting through a fake KMS client: no Google Cloud project or credentials needed.</summary>
public class GcpKmsMasterKeyTests
{
    private sealed class FakeKms(Func<DecryptRequest, DecryptResponse> onDecrypt) : KeyManagementServiceClient
    {
        public DecryptRequest? LastRequest { get; private set; }

        public override Task<DecryptResponse> DecryptAsync(DecryptRequest request, Google.Api.Gax.Grpc.CallSettings? callSettings = null)
        {
            LastRequest = request;
            return Task.FromResult(onDecrypt(request));
        }
    }

    private static string Enc(params byte[] bytes) => Convert.ToBase64String(bytes);

    private const string Resource = "projects/p/locations/global/keyRings/kr/cryptoKeys/k";

    [Fact]
    public void Properties_ReflectTheConstructorArguments()
    {
        var client = new FakeKms(_ => new DecryptResponse());

        var key = new GcpKmsMasterKey(Resource, "share", client);

        Assert.Equal("gcp_kms", key.KeyType);
        Assert.Equal(Resource, key.ResourceName);
        Assert.Equal("share", key.EncryptedShare);
        Assert.Same(client, key.Client);
    }

    [Fact]
    public async Task DecryptShare_SendsResourceAndCiphertext_AndReturnsPlaintext()
    {
        var client = new FakeKms(_ => new DecryptResponse { Plaintext = ByteString.CopyFrom(9, 8, 7) });
        var key = new GcpKmsMasterKey(Resource, client: client);

        var data = await key.DecryptShareAsync(Enc(1, 2, 3));

        Assert.Equal(new byte[] { 9, 8, 7 }, data);
        Assert.Equal(Resource, client.LastRequest!.Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, client.LastRequest.Ciphertext.ToByteArray());
    }

    [Fact]
    public async Task DecryptShare_PermissionDenied_ReturnsNull()
    {
        var client = new FakeKms(_ => throw new RpcException(new Status(StatusCode.PermissionDenied, "no")));

        Assert.Null(await new GcpKmsMasterKey(Resource, client: client).DecryptShareAsync(Enc(1)));
    }

    [Fact]
    public async Task DecryptShare_OtherFailure_ReturnsNull()
    {
        var client = new FakeKms(_ => throw new InvalidOperationException("boom"));

        Assert.Null(await new GcpKmsMasterKey(Resource, client: client).DecryptShareAsync(Enc(1)));
    }

    [Fact]
    public async Task DecryptShare_InvalidBase64_ReturnsNull()
    {
        var client = new FakeKms(_ => throw new InvalidOperationException("must not be called"));

        Assert.Null(await new GcpKmsMasterKey(Resource, client: client).DecryptShareAsync("not base64 !!"));
    }
}
