using System.Reflection;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Nops.Core.Keys;

namespace Nops.Core.AwsKms.Tests;

/// <summary>Decrypting through a fake KMS client: no AWS account, network or credentials needed.</summary>
public class AwsKmsMasterKeyTests
{
    /// <summary>Stand-in for <see cref="IAmazonKeyManagementService"/> that only implements DecryptAsync.</summary>
    public class FakeKms : DispatchProxy
    {
        public Func<DecryptRequest, Task<DecryptResponse>> OnDecrypt { get; set; } = _ => throw new NotSupportedException();
        public DecryptRequest? LastRequest { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "DecryptAsync")
            {
                LastRequest = (DecryptRequest)args![0]!;
                return OnDecrypt(LastRequest);
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private static (IAmazonKeyManagementService Client, FakeKms Fake) NewClient(Func<DecryptRequest, Task<DecryptResponse>> onDecrypt)
    {
        var client = DispatchProxy.Create<IAmazonKeyManagementService, FakeKms>();
        var fake = (FakeKms)(object)client;
        fake.OnDecrypt = onDecrypt;
        return (client, fake);
    }

    private static string Enc(params byte[] bytes) => Convert.ToBase64String(bytes);

    [Fact]
    public void Properties_ReflectTheConstructorArguments()
    {
        var (client, _) = NewClient(_ => throw new NotSupportedException());
        var context = new[] { new KeyValuePair<string, string>("app", "nops") };

        var key = new AwsKmsMasterKey("arn:aws:kms:eu-west-1:1:key/abc", "share", context, client);

        Assert.Equal("aws_kms", key.KeyType);
        Assert.Equal("arn:aws:kms:eu-west-1:1:key/abc", key.Arn);
        Assert.Equal("share", key.EncryptedShare);
        Assert.Equal(context, key.Context);
        Assert.Same(client, key.Client);
    }

    [Fact]
    public void Defaults_AreEmpty()
    {
        var key = new AwsKmsMasterKey("arn");

        Assert.Empty(key.Context);
        Assert.Null(key.Client);
        Assert.Equal("", key.EncryptedShare);
    }

    [Fact]
    public async Task DecryptShare_SendsKeyAndCiphertext_AndReturnsPlaintext()
    {
        var (client, fake) = NewClient(_ => Task.FromResult(new DecryptResponse { Plaintext = new MemoryStream([9, 8, 7]) }));
        var key = new AwsKmsMasterKey("arn:key", client: client);

        var data = await key.DecryptShareAsync(Enc(1, 2, 3));

        Assert.Equal(new byte[] { 9, 8, 7 }, data);
        Assert.Equal("arn:key", fake.LastRequest!.KeyId);
        Assert.Equal(new byte[] { 1, 2, 3 }, fake.LastRequest.CiphertextBlob.ToArray());
        Assert.Empty(fake.LastRequest.EncryptionContext ?? []);
    }

    [Fact]
    public async Task DecryptShare_SendsTheEncryptionContext()
    {
        var (client, fake) = NewClient(_ => Task.FromResult(new DecryptResponse { Plaintext = new MemoryStream([1]) }));
        var key = new AwsKmsMasterKey("arn:key", context: [new("app", "nops"), new("env", "prod")], client: client);

        await key.DecryptShareAsync(Enc(1));

        Assert.Equal("nops", fake.LastRequest!.EncryptionContext["app"]);
        Assert.Equal("prod", fake.LastRequest.EncryptionContext["env"]);
    }

    [Fact]
    public async Task DecryptShare_WithoutPlaintext_ReturnsNull()
    {
        var (client, _) = NewClient(_ => Task.FromResult(new DecryptResponse()));

        Assert.Null(await new AwsKmsMasterKey("arn", client: client).DecryptShareAsync(Enc(1)));
    }

    [Fact]
    public async Task DecryptShare_KmsError_ReturnsNull()
    {
        var (client, _) = NewClient(_ => throw new AmazonKeyManagementServiceException("denied"));

        Assert.Null(await new AwsKmsMasterKey("arn", client: client).DecryptShareAsync(Enc(1)));
    }

    [Fact]
    public async Task DecryptShare_OtherFailure_ReturnsNull()
    {
        var (client, _) = NewClient(_ => throw new HttpRequestException("network down"));

        Assert.Null(await new AwsKmsMasterKey("arn", client: client).DecryptShareAsync(Enc(1)));
    }

    [Fact]
    public async Task DecryptShare_InvalidBase64_ReturnsNull()
    {
        var (client, _) = NewClient(_ => throw new InvalidOperationException("must not be called"));

        Assert.Null(await new AwsKmsMasterKey("arn", client: client).DecryptShareAsync("not base64 !!"));
    }
}
