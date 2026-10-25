using System.Security.Cryptography;
using System.Text;
using Nops.Core.Keys;

namespace Nops.Core.Tests.Keys;

/// <summary>
/// Runs the age decryptor against the C2SP/CCTV age test vectors that use an X25519 identity.
/// Every vector that is not <c>expect: success</c> must be rejected.
/// </summary>
public class AgeCctvTests
{
    private static readonly string VectorDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "age-cctv");

    public static IEnumerable<object[]> Vectors() =>
        Directory.GetFiles(VectorDir)
            .Where(f => Path.GetFileName(f) != "README.md")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void VectorsArePresent() => Assert.True(Vectors().Count() >= 100);

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Vector(string name)
    {
        var raw = File.ReadAllBytes(Path.Combine(VectorDir, name));
        var split = IndexOf(raw, "\n\n"u8);
        Assert.True(split > 0, "vector has no header terminator");

        var headers = Encoding.UTF8.GetString(raw, 0, split)
            .Split('\n')
            .Select(l => l.Split(": ", 2))
            .ToLookup(p => p[0], p => p.Length > 1 ? p[1] : "");
        var body = raw[(split + 2)..];
        if (headers["compressed"].FirstOrDefault() == "zlib")
        {
            using var inflater = new System.IO.Compression.ZLibStream(new MemoryStream(body), System.IO.Compression.CompressionMode.Decompress);
            var inflated = new MemoryStream();
            inflater.CopyTo(inflated);
            body = inflated.ToArray();
        }

        var identity = headers["identity"].Single();
        Assert.True(AgeDecryptor.TryParseIdentity(identity, out var scalar));

        var expect = headers["expect"].Single();
        if (expect == "success")
        {
            var plaintext = AgeDecryptor.Decrypt(body, scalar);
            Assert.Equal(headers["payload"].Single(), Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant());
        }
        else
        {
            var ex = Assert.ThrowsAny<Exception>(() => AgeDecryptor.Decrypt(body, scalar));
            Assert.True(
                ex is InvalidDataException or AgeDecryptor.AgeIdentityMismatchException,
                $"unexpected exception type {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle);
}
