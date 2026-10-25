using Nops.Core.Keys;

namespace Nops.Core.Tests.Keys;

/// <summary>
/// PGP key handling beyond the happy path. <c>PGP-PASSPHRASE-KEY.*</c> is a TEST-ONLY secret key protected with
/// the passphrase below; <c>pgp-passphrase.enc.yaml</c> is a file written by real sops for it. (It is not named
/// <c>*.sops.*</c> because the CI fixture check decrypts those with gpg, which cannot ask for a passphrase.)
/// </summary>
public class PgpMasterKeyTests
{
    private const string Passphrase = "nops-test-passphrase";
    private const string Fingerprint = "5B3E2BB86CDF737A3B585240C0914CD66DE561F6";

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static Task<IReadOnlyDictionary<string, string>> Decrypt(PgpMasterKey key) =>
        SopsFile.DecryptToDictionaryAsync(Fixture("pgp-passphrase.enc.yaml"), "yaml",
            new SopsOptions { MasterKeys = [key], UseSopsEnvironment = false });

    [Fact]
    public void Properties_ReflectTheConstructorArguments()
    {
        var key = new PgpMasterKey("ABCD", [1, 2], "pw");

        Assert.Equal("pgp", key.KeyType);
        Assert.Equal("ABCD", key.Fingerprint);
        Assert.Equal(new byte[] { 1, 2 }, key.PrivateKeyBytes);
        Assert.Equal("pw", key.Passphrase);
    }

    [Fact]
    public async Task CorrectPassphrase_Decrypts()
    {
        var values = await Decrypt(new PgpMasterKey(Fingerprint, Fixture("PGP-PASSPHRASE-KEY.asc"), Passphrase));

        Assert.Equal("passphrase-protected-secret", values["db_password"]);
    }

    [Fact]
    public async Task BinaryKeyExport_Decrypts()
    {
        var values = await Decrypt(new PgpMasterKey(Fingerprint, Fixture("PGP-PASSPHRASE-KEY.gpg"), Passphrase));

        Assert.Equal("passphrase-protected-secret", values["db_password"]);
    }

    [Fact]
    public async Task MissingPassphrase_ExplainsWhatToDo()
    {
        var key = new PgpMasterKey(Fingerprint, Fixture("PGP-PASSPHRASE-KEY.asc"));

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => Decrypt(key));

        Assert.Contains("no passphrase was supplied", ex.Message);
    }

    [Fact]
    public async Task WrongPassphrase_IsAnOrdinaryFailure()
    {
        var key = new PgpMasterKey(Fingerprint, Fixture("PGP-PASSPHRASE-KEY.asc"), "wrong");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Decrypt(key));

        Assert.Contains("Cannot decrypt", ex.Message);
    }

    [Fact]
    public async Task KeyIdOnly_MatchesTheRing()
    {
        var values = await Decrypt(new PgpMasterKey("C0914CD66DE561F6", Fixture("PGP-PASSPHRASE-KEY.asc"), Passphrase));

        Assert.Equal("passphrase-protected-secret", values["db_password"]);
    }

    [Theory]
    [InlineData("zzzzzzzzzzzzzzzz")]   // right length, not hex
    [InlineData("not-hex")]            // too short to be a key id
    public async Task MalformedFingerprint_MatchesNothing(string fingerprint)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Decrypt(new PgpMasterKey(fingerprint, Fixture("PGP-PASSPHRASE-KEY.asc"), Passphrase)));

        Assert.Contains("Cannot decrypt", ex.Message);
    }

    [Fact]
    public async Task EmptyFingerprint_MatchesAnyKeyInTheRing()
    {
        var values = await Decrypt(new PgpMasterKey("", Fixture("PGP-PASSPHRASE-KEY.asc"), Passphrase));

        Assert.Equal("passphrase-protected-secret", values["db_password"]);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("not a pgp message", true)]
    [InlineData("-----BEGIN PGP MESSAGE-----\n\nAAAA\n-----END PGP MESSAGE-----", true)]
    public async Task UndecryptableShare_ReturnsNull(string enc, bool withKey)
    {
        var key = new PgpMasterKey(Fingerprint, withKey ? Fixture("PGP-PASSPHRASE-KEY.asc") : null, Passphrase);

        Assert.Null(await key.DecryptShareAsync(enc));
    }

    [Fact]
    public async Task WithoutKeyMaterial_ReturnsNull()
    {
        Assert.Null(await new PgpMasterKey(Fingerprint).DecryptShareAsync("anything"));
        Assert.Null(await new PgpMasterKey(Fingerprint, []).DecryptShareAsync("anything"));
    }

    [Fact]
    public async Task GarbageKeyBytes_ReturnNull()
    {
        var key = new PgpMasterKey(Fingerprint, [1, 2, 3, 4], Passphrase);

        Assert.Null(await key.DecryptShareAsync("-----BEGIN PGP MESSAGE-----\n\nAAAA\n-----END PGP MESSAGE-----"));
    }
}
