using System.Text;

namespace Nops.Core.Tests;

/// <summary>SopsFile behaviour on typed values, unusual shapes and invalid files.</summary>
public class SopsFileEdgeCaseTests
{
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixtureDir, name));

    private static SopsOptions AgeOptions() => new()
    {
        AgeIdentities = [Path.Combine(FixtureDir, "AGE-TEST-KEY.txt")],
        UseSopsEnvironment = false,
    };

    [Fact]
    public async Task TypedValues_AreFlattenedLikeConfiguration()
    {
        var values = await SopsFile.DecryptToDictionaryAsync(Fixture("typed-values.sops.yaml"), "yaml", AgeOptions());

        Assert.Equal("hello bytes", values["blob"]);
        Assert.Equal("plain", values["items:1"]);
        Assert.Equal("7", values["items:2"]);
        Assert.Equal("1", values["matrix:0:0"]);
        Assert.Equal("b", values["matrix:1:1"]);
        Assert.Equal("0.25", values["ratio"]);
        Assert.Equal("True", values["flag"]);
        Assert.Equal("2024-05-06T07:08:09Z", values["when"]);
        Assert.DoesNotContain("nothing", values.Keys);
        Assert.DoesNotContain("empty_list", values.Keys);
    }

    private sealed class Typed
    {
        public string Blob { get; set; } = "";
        public double Ratio { get; set; }
        public bool Flag { get; set; }
        public List<List<object>> Matrix { get; set; } = [];
        public DateTimeOffset When { get; set; }
    }

    [Fact]
    public async Task TypedBinding_UsesTheOriginalTypes()
    {
        var typed = await SopsFile.DecryptAsync<Typed>(Fixture("typed-values.sops.yaml"), "yaml", AgeOptions());

        Assert.Equal(0.25, typed.Ratio);
        Assert.True(typed.Flag);
        Assert.Equal(2, typed.Matrix.Count);
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero), typed.When);
    }

    [Fact]
    public async Task UnknownFileKind_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SopsFile.DecryptToDictionaryAsync(Fixture("secrets.sops.yaml"), "toml", AgeOptions()));

        Assert.Contains("Unknown file kind: toml", ex.Message);
    }

    [Fact]
    public async Task PgpKeysOption_IsUsedToDecrypt()
    {
        var options = new SopsOptions
        {
            PgpKeys = [(Fixture("PGP-TEST-KEY.asc"), null)],
            UseSopsEnvironment = false,
        };

        var values = await SopsFile.DecryptToDictionaryAsync(Fixture("pgp.sops.yaml"), "yaml", options);

        Assert.Equal("pgp-secret", values["db_password"]);
    }

    [Fact]
    public async Task PlaintextWhereCiphertextIsExpected_IsRejected()
    {
        var yaml = Encoding.UTF8.GetString(Fixture("secrets.sops.yaml"));
        var tampered = System.Text.RegularExpressions.Regex.Replace(yaml, @"^myapp1: .*$", "myapp1: now plaintext", System.Text.RegularExpressions.RegexOptions.Multiline);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SopsFile.DecryptToDictionaryAsync(Encoding.UTF8.GetBytes(tampered), "yaml", AgeOptions()));

        Assert.Contains("got plaintext value", ex.Message);
    }

    [Fact]
    public async Task NonStringWhereCiphertextIsExpected_IsRejected()
    {
        var yaml = Encoding.UTF8.GetString(Fixture("secrets.sops.yaml"));
        var tampered = System.Text.RegularExpressions.Regex.Replace(yaml, @"^myapp1: .*$", "myapp1: 5", System.Text.RegularExpressions.RegexOptions.Multiline);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SopsFile.DecryptToDictionaryAsync(Encoding.UTF8.GetBytes(tampered), "yaml", AgeOptions()));

        Assert.Contains("Expected encrypted value as string", ex.Message);
    }

    [Fact]
    public async Task MacMismatch_CanBeIgnoredExplicitly()
    {
        var yaml = Encoding.UTF8.GetString(Fixture("secrets.sops.yaml"));
        // The file's MAC covers every value; dropping a key makes it stale without breaking decryption.
        var edited = System.Text.RegularExpressions.Regex.Replace(yaml, @"^number: .*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        var options = AgeOptions();

        await Assert.ThrowsAsync<MacMismatchException>(() =>
            SopsFile.DecryptToDictionaryAsync(Encoding.UTF8.GetBytes(edited), "yaml", options));

        options.ThrowOnMacMismatch = false;
        var values = await SopsFile.DecryptToDictionaryAsync(Encoding.UTF8.GetBytes(edited), "yaml", options);
        Assert.Equal("hello world", values["myapp1"]);
    }
}

public class TamperedValueTests
{
    [Fact]
    public async Task ATamperedEncryptedValue_RaisesMacMismatch_NotAThirdPartyException()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var yaml = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir, "secrets.sops.yaml")));
        // Change one character of the ciphertext of the first value.
        var tampered = System.Text.RegularExpressions.Regex.Replace(
            yaml, @"(myapp1: ENC\[AES256_GCM,data:)(.)", m => m.Groups[1].Value + (m.Groups[2].Value == "A" ? "B" : "A"));
        Assert.NotEqual(yaml, tampered);

        var options = new SopsOptions { AgeIdentities = [Path.Combine(dir, "AGE-TEST-KEY.txt")], UseSopsEnvironment = false };

        var ex = await Assert.ThrowsAsync<MacMismatchException>(() =>
            SopsFile.DecryptToDictionaryAsync(System.Text.Encoding.UTF8.GetBytes(tampered), "yaml", options));
        Assert.Contains("failed authentication", ex.Message);
    }
}
