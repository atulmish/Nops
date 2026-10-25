using System.Globalization;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Nops.Core.Crypto;
using Nops.Core.Keys;
using Nops.Core.Stores;

namespace Nops.Core.Tests;

/// <summary>
/// Property-based tests: FsCheck generates hundreds of random inputs and checks laws that must hold for every
/// one of them. A failure prints the seed that reproduces it. They complement the fixed examples and the
/// mutation test by exploring inputs nobody thought of.
/// </summary>
public class PropertyBasedTests
{
    private const int Tests = 500;

    private static void Run(Property property) =>
        Check.One(Config.QuickThrowOnFailure.WithMaxTest(Tests), property);

    private static Arbitrary<string> Strings => ArbMap.Default.ArbFor<string>();

    private static Arbitrary<byte[]> Bytes => ArbMap.Default.ArbFor<byte[]>();

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

    // ---- age -------------------------------------------------------------------------------------------

    [Fact]
    public void Bech32DecodingNeverThrows() =>
        Run(Prop.ForAll(Strings, text =>
        {
            AgeBech32.TryDecode(text ?? string.Empty, out _, out _);
            return true;
        }));

    [Fact]
    public void ParsingAnIdentityNeverThrows() =>
        Run(Prop.ForAll(Strings, text =>
        {
            AgeDecryptor.TryParseIdentity(text ?? string.Empty, out _);
            return true;
        }));

    [Fact]
    public void DecryptingArbitraryBytesFailsOnlyWithAgeErrors()
    {
        AgeDecryptor.TryParseIdentity("AGE-SECRET-KEY-1EGTZVFFV20835NWYV6270LXYVK2VKNX2MMDKWYKLMGR48UAWX40Q2P2LM0", out var identity);
        var intro = Encoding.ASCII.GetBytes("age-encryption.org/v1\n");

        // Half of the inputs start with a valid intro line so that the header parser is reached.
        var inputs =
            from bytes in Bytes.Generator
            from withIntro in Gen.Elements(true, false)
            select withIntro ? [.. intro, .. bytes ?? []] : bytes ?? [];

        Run(Prop.ForAll(inputs.ToArbitrary(), input =>
        {
            try
            {
                AgeDecryptor.Decrypt(input, identity);
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException or AgeDecryptor.AgeIdentityMismatchException)
            {
                return true;
            }
        }));
    }

    // ---- number and value encoding ---------------------------------------------------------------------

    private static readonly Gen<double> FiniteDoubles =
        ArbMap.Default.GeneratorFor<long>()
            .Select(BitConverter.Int64BitsToDouble)
            .Where(double.IsFinite);

    [Fact]
    public void EncodedDoublesParseBackExactly() =>
        // sops hashes floats with Go's shortest round-trip format, so the text must read back to the same number.
        Run(Prop.ForAll(FiniteDoubles.ToArbitrary(), value =>
        {
            var text = Encoding.UTF8.GetString(ToBytes.Convert(value));

            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(value)
                || (value == 0 && double.Parse(text, CultureInfo.InvariantCulture) == 0);
        }));

    [Fact]
    public void EncodedFloatsParseBackExactly() =>
        Run(Prop.ForAll(FiniteDoubles.Select(d => (float)d).Where(float.IsFinite).ToArbitrary(), value =>
        {
            var text = Encoding.UTF8.GetString(ToBytes.Convert(value));

            return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(value)
                || (value == 0 && float.Parse(text, CultureInfo.InvariantCulture) == 0);
        }));

    [Fact]
    public void EncodedIntegersAreTheirDecimalText() =>
        Run(Prop.ForAll(ArbMap.Default.ArbFor<long>(), value =>
            Encoding.UTF8.GetString(ToBytes.Convert(value)) == value.ToString(CultureInfo.InvariantCulture)));

    [Fact]
    public void DotenvNewlineEscapingRoundTripsForEveryValueWithoutALiteralBackslashN() =>
        // The sops dotenv format writes a newline as the two characters \n and reads them back as a newline.
        // That is lossy for a value that already contains a literal backslash followed by n, in sops itself as
        // well, so the law holds for every other value (see the example below).
        Run(Prop.ForAll(Strings, text =>
        {
            text ??= string.Empty;
            return text.Contains("\\n", StringComparison.Ordinal)
                || DotenvStore.UnescapeNewlines(DotenvStore.EscapeNewlines(text)) == text;
        }));

    [Fact]
    public void ALiteralBackslashNInADotenvValueReadsBackAsANewlineLikeSops() =>
        Assert.Equal("a\nb", DotenvStore.UnescapeNewlines("a\\nb"));

    [Fact]
    public void ParsingAnEncryptedValueNeverThrows() =>
        Run(Prop.ForAll(Strings, text =>
        {
            EncryptedValueParser.TryParse(text ?? string.Empty, out _);
            return true;
        }));

    // ---- Shamir secret sharing -------------------------------------------------------------------------

    [Fact]
    public void AnyThresholdSizedSubsetOfSharesRecoversTheSecret()
    {
        var inputs =
            from secret in Gen.ListOf(Gen.Choose(0, 255).Select(i => (byte)i), 32)
            from shares in Gen.Choose(2, 8)
            from threshold in Gen.Choose(2, shares)
            from seed in Gen.Choose(0, int.MaxValue)
            select (secret: secret.ToArray(), shares, threshold, seed);

        Run(Prop.ForAll(inputs.ToArbitrary(), input =>
        {
            var parts = Shamir.Split(input.secret, input.shares, input.threshold);
            var random = new Random(input.seed);
            var subset = parts.OrderBy(_ => random.Next()).Take(input.threshold).ToArray();

            return Shamir.Combine(subset).SequenceEqual(input.secret);
        }));
    }

    // ---- file formats ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("yaml")]
    [InlineData("json")]
    [InlineData("dotenv")]
    [InlineData("ini")]
    [InlineData("binary")]
    public async Task LoadingArbitraryTextFailsOnlyWithDocumentedErrors(string kind)
    {
        var options = new SopsOptions { UseSopsEnvironment = false };
        var rejected = new List<string>();

        // Property generation is synchronous, so collect any undocumented failure and assert afterwards.
        Run(Prop.ForAll(Strings, text =>
        {
            try
            {
                SopsFile.DecryptToDictionaryAsync(Encoding.UTF8.GetBytes(text ?? string.Empty), kind, options)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex) when (IsDocumented(ex))
            {
            }
            catch (Exception ex)
            {
                rejected.Add($"{ex.GetType().Name}: {ex.Message} for input {Escape(text)}");
            }

            return true;
        }));

        await Task.CompletedTask;
        Assert.True(rejected.Count == 0, string.Join(Environment.NewLine, rejected.Take(5)));
    }

    private static string Escape(string? text) =>
        text is null ? "null" : "\"" + text.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal) + "\"";
}
