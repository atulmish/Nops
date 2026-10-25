using Nops.Core.Keys;

namespace Nops.Core.Tests;

/// <summary>
/// Feeds thousands of corrupted copies of valid files to the decryption code and asserts that every one
/// either decrypts or fails with a documented exception type. A NullReferenceException, an
/// IndexOutOfRangeException or similar means a malformed file reached code that did not expect it.
/// The mutations are seeded, so a failure is reproducible.
/// </summary>
public class RobustnessTests
{
    private const int Iterations = 1500;
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static byte[] Mutate(byte[] original, Random random)
    {
        var data = original.ToList();
        var operations = 1 + random.Next(3);
        for (var i = 0; i < operations && data.Count > 0; i++)
        {
            var at = random.Next(data.Count);
            switch (random.Next(6))
            {
                case 0: data[at] = (byte)random.Next(256); break;                                   // overwrite a byte
                case 1: data[at] ^= (byte)(1 << random.Next(8)); break;                              // flip a bit
                case 2: data.RemoveRange(at, data.Count - at); break;                                // truncate
                case 3: data.RemoveRange(at, Math.Min(random.Next(1, 40), data.Count - at)); break; // delete a range
                case 4: data.InsertRange(at, Enumerable.Range(0, random.Next(1, 20)).Select(_ => (byte)random.Next(256))); break;
                default:                                                                             // duplicate a range
                    var length = Math.Min(random.Next(1, 60), data.Count - at);
                    data.InsertRange(at, data.GetRange(at, length));
                    break;
            }
        }

        return [.. data];
    }

    private static bool IsDocumented(Exception ex) => ex is
        InvalidDataException or
        InvalidOperationException or
        MacMismatchException or
        SopsMetadataException or
        NotSupportedException or
        ArgumentException or
        FormatException or
        System.Text.Json.JsonException or
        YamlDotNet.Core.YamlException or
        AgeDecryptor.AgeIdentityMismatchException;

    [Fact]
    public void CorruptedAgeFiles_FailOnlyWithAgeErrors()
    {
        var vector = File.ReadAllBytes(Path.Combine(FixtureDir, "age-cctv", "armor_x25519"));
        var body = vector[(System.Text.Encoding.Latin1.GetString(vector).IndexOf("\n\n", StringComparison.Ordinal) + 2)..];
        Assert.True(AgeDecryptor.TryParseIdentity(
            "AGE-SECRET-KEY-1EGTZVFFV20835NWYV6270LXYVK2VKNX2MMDKWYKLMGR48UAWX40Q2P2LM0", out var scalar));
        var random = new Random(20261025);

        var accepted = 0;
        for (var i = 0; i < Iterations * 4; i++)
        {
            var mutated = Mutate(body, random);
            try
            {
                AgeDecryptor.Decrypt(mutated, scalar);
                accepted++;
            }
            catch (Exception ex) when (ex is InvalidDataException or AgeDecryptor.AgeIdentityMismatchException)
            {
            }
        }

        // A few mutations are harmless (armor whitespace, for example). Almost none should be accepted.
        Assert.True(accepted < Iterations / 10, $"{accepted} corrupted files were accepted");
    }

    [Theory]
    [InlineData("secrets.sops.yaml", "yaml")]
    [InlineData("secrets.sops.json", "json")]
    [InlineData("dotenv.sops.env", "dotenv")]
    [InlineData("ini.sops.ini", "ini")]
    [InlineData("nested.sops.yaml", "yaml")]
    [InlineData("typed-values.sops.yaml", "yaml")]
    public async Task CorruptedSopsFiles_FailOnlyWithDocumentedErrors(string fixture, string kind)
    {
        var original = File.ReadAllBytes(Path.Combine(FixtureDir, fixture));
        var options = new SopsOptions
        {
            AgeIdentities = [Path.Combine(FixtureDir, "AGE-TEST-KEY.txt")],
            UseSopsEnvironment = false,
        };
        var random = new Random(fixture.GetHashCode() ^ 20261025);

        for (var i = 0; i < Iterations; i++)
        {
            var mutated = Mutate(original, random);
            try
            {
                await SopsFile.DecryptToDictionaryAsync(mutated, kind, options);
            }
            catch (Exception ex) when (IsDocumented(ex))
            {
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"{fixture}, iteration {i}: unexpected {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
