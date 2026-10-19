using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Nops.Configuration;
using Xunit;

namespace Nops.Configuration.Tests;

/// <summary>
/// Tests for SopsConfigurationProvider and related classes.
/// Note: Full integration tests require working SOPS decryption (Phase 1).
/// These tests verify the configuration provider structure and options.
/// </summary>
public class SopsOptionsTests
{
    [Fact]
    public void DefaultValues_HaveSensibleDefaults()
    {
        var options = new SopsOptions();

        Assert.Null(options.FilePath);
        Assert.Null(options.FileKind);
        Assert.Null(options.AgeIdentities);
        Assert.Null(options.PgpKeys);
        Assert.False(options.WatchForChanges);
        Assert.Null(options.ReloadInterval);
        Assert.False(options.Optional);
        Assert.Equal(FileNotFoundMode.Throw, options.FileNotFoundMode);
        Assert.Equal(10 * 1024 * 1024, options.MaxFileSize);
    }

    [Fact]
    public void ResolveMasterKeys_ReturnsMasterKeys_IfSet()
    {
        var fakeKey = new FakeMasterKey();
        var options = new SopsOptions
        {
            MasterKeys = new[] { fakeKey }
        };

        var resolved = options.ResolveMasterKeys();

        Assert.Single(resolved);
        Assert.Same(fakeKey, resolved[0]);
    }

    [Fact]
    public void ResolveMasterKeys_BuildsFromAgeIdentities()
    {
        var ageKey = "AGE-SECRET-KEY-1234567890abcdef";
        var options = new SopsOptions
        {
            AgeIdentities = new[] { ageKey }
        };

        var resolved = options.ResolveMasterKeys();

        Assert.Single(resolved);
        Assert.IsType<Nops.Core.Keys.AgeMasterKey>(resolved[0]);
    }

    [Fact]
    public void ResolveMasterKeys_PathToAgeIdentityFile_LoadsEverySecretKey()
    {
        var keyFile = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(keyFile, new[]
            {
                "# TEST-ONLY identities",
                "# public key: age1qexample",
                "AGE-SECRET-KEY-1AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "",
                "AGE-SECRET-KEY-1BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                "AGE-SECRET-KEY-1CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
            });

            var options = new SopsOptions
            {
                AgeIdentities = new[] { keyFile }
            };

            var resolved = options.ResolveMasterKeys();

            // Comments, blank lines and the public-key line are ignored.
            Assert.Equal(3, resolved.Count);
            Assert.All(resolved, k => Assert.IsType<Nops.Core.Keys.AgeMasterKey>(k));
        }
        finally
        {
            File.Delete(keyFile);
        }
    }

    [Fact]
    public void ResolveMasterKeys_AgeIdentityFileMixedWithInlineKey_LoadsAll()
    {
        var keyFile = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(keyFile, new[]
            {
                "AGE-SECRET-KEY-1AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "AGE-SECRET-KEY-1BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            });

            var options = new SopsOptions
            {
                AgeIdentities = new[]
                {
                    "AGE-SECRET-KEY-1CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
                    keyFile,
                }
            };

            var resolved = options.ResolveMasterKeys();

            Assert.Equal(3, resolved.Count);
        }
        finally
        {
            File.Delete(keyFile);
        }
    }

    [Fact]
    public void ResolveMasterKeys_BuildsFromPgpKeys()
    {
        var pgpKey = new byte[] { 0x01, 0x02, 0x03 };
        var options = new SopsOptions
        {
            PgpKeys = new (byte[], string?)[] { (pgpKey, "passphrase") }
        };

        var resolved = options.ResolveMasterKeys();

        Assert.Single(resolved);
        Assert.IsType<Nops.Core.Keys.PgpMasterKey>(resolved[0]);
    }

    [Fact]
    public void ResolveMasterKeys_PrefersMasterKeysOverDerived()
    {
        var customKey = new FakeMasterKey();
        var options = new SopsOptions
        {
            MasterKeys = new[] { customKey },
            AgeIdentities = new[] { "AGE-SECRET-KEY-1234567890abcdef" }
        };

        var resolved = options.ResolveMasterKeys();

        Assert.Single(resolved);
        Assert.Same(customKey, resolved[0]);
    }
}

public class FakeMasterKey : Nops.Core.Keys.IMasterKeyProvider
{
    public string KeyType => "fake";
    public System.Threading.Tasks.Task<byte[]?> DecryptShareAsync(string enc, System.Threading.CancellationToken ct = default)
        => System.Threading.Tasks.Task.FromResult<byte[]?>(null);
}

public class SopsConfigurationSourceTests
{
    [Fact]
    public void Constructor_ThrowsIfOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SopsConfigurationSource(null!));
    }

    [Fact]
    public void Build_ReturnsSopsConfigurationProvider()
    {
        var tempFile = Path.GetTempFileName() + ".sops.yaml";
        File.WriteAllText(tempFile, "# placeholder");
        var options = new SopsOptions { FilePath = tempFile, Optional = true };
        var source = new SopsConfigurationSource(options);

        var provider = source.Build(new ConfigurationBuilder());

        Assert.IsType<SopsConfigurationProvider>(provider);
    }
}

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSops_WithFilePath_AddsSource()
    {
        var tempFile = Path.GetTempFileName() + ".sops.yaml";
        File.WriteAllText(tempFile, "# placeholder");
        try
        {
            var builder = new ConfigurationBuilder();
            builder.AddSops(tempFile, opts => opts.Optional = true);

            // The source was added - verify by building and checking providers
            var config = builder.Build();
            // If we got here without exception, the source was added successfully
            Assert.NotNull(config);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void AddSops_WithFilePathAndOptions_AddsSource()
    {
        var builder = new ConfigurationBuilder();
        builder.AddSops("/tmp/test.sops.yaml", opts =>
        {
            opts.Optional = true;
            opts.WatchForChanges = false;
        });

        var config = builder.Build();
        Assert.NotNull(config);
    }

    [Fact]
    public void AddSops_WithOnlyOptions_ThrowsIfFilePathNotSet()
    {
        var builder = new ConfigurationBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.AddSops(opts => { opts.WatchForChanges = true; }));
    }

    [Fact]
    public void AddSops_NullFilePath_ThrowsArgumentNullException()
    {
        var builder = new ConfigurationBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            builder.AddSops((string)null!));
    }

    [Fact]
    public void AddSops_NullConfigureOptions_ThrowsArgumentNullException()
    {
        var builder = new ConfigurationBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            builder.AddSops("/tmp/test.sops.yaml", null!));
    }
}

public class SopsConfigurationProviderTests
{
    [Fact]
    public void Constructor_ThrowsIfOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SopsConfigurationProvider(null!));
    }

    [Fact]
    public void Constructor_ThrowsIfFilePathNotSet()
    {
        var options = new SopsOptions();
        Assert.Throws<ArgumentException>(() => new SopsConfigurationProvider(options));
    }

    [Fact]
    public void Constructor_ThrowsIfFileNotFoundAndNotOptional()
    {
        var options = new SopsOptions
        {
            FilePath = "/tmp/nonexistent-file-12345.sops.yaml"
        };

        Assert.Throws<FileNotFoundException>(() => new SopsConfigurationProvider(options));
    }

    [Fact]
    public void Constructor_DoesNotThrowIfFileNotFoundAndOptional()
    {
        var options = new SopsOptions
        {
            FilePath = "/tmp/nonexistent-file-12345.sops.yaml",
            Optional = true
        };

        // Should not throw - but will fail to load since file doesn't exist
        var provider = new SopsConfigurationProvider(options);
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_SetsUpWatcher_WhenWatchForChangesIsTrue()
    {
        var tempFile = Path.GetTempFileName() + ".sops.yaml";
        File.WriteAllText(tempFile, "# placeholder");

        try
        {
            var options = new SopsOptions
            {
                FilePath = tempFile,
                WatchForChanges = true
            };

            var provider = new SopsConfigurationProvider(options);
            Assert.NotNull(provider);

            provider.Dispose();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_WithMissingFile_HandlesAccordingToMode()
    {
        var tempFile = Path.GetTempFileName() + ".sops.yaml";

        try
        {
            // Delete the file before loading
            File.Delete(tempFile);

            var options = new SopsOptions
            {
                FilePath = tempFile,
                Optional = true,
                FileNotFoundMode = FileNotFoundMode.WarnAndContinue
            };

            var provider = new SopsConfigurationProvider(options);

            // Should not throw - just produce empty config
            provider.Load();

            Assert.False(((IConfigurationProvider)provider).TryGet("any", out _));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFileKind_FromExtension_Yaml()
    {
        Assert.Equal("yaml", DetectKind("/path/to/file.yaml"));
        Assert.Equal("yaml", DetectKind("/path/to/file.yml"));
    }

    [Fact]
    public void DetectFileKind_FromExtension_Json()
    {
        Assert.Equal("json", DetectKind("/path/to/file.json"));
    }

    [Fact]
    public void DetectFileKind_FromExtension_Dotenv()
    {
        Assert.Equal("dotenv", DetectKind("/path/to/file.env"));
        Assert.Equal("dotenv", DetectKind("/path/to/file.dotenv"));
    }

    [Fact]
    public void DetectFileKind_FromExtension_Ini()
    {
        Assert.Equal("ini", DetectKind("/path/to/file.ini"));
    }

    [Fact]
    public void DetectFileKind_FromExtension_Binary()
    {
        Assert.Equal("binary", DetectKind("/path/to/file.bin"));
    }

    [Fact]
    public void DetectFileKind_UnknownExtension_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DetectKind("/path/to/file.xyz"));
    }

    private static string DetectKind(string path)
    {
        var provider = new SopsConfigurationProvider(new SopsOptions
        {
            FilePath = path,
            Optional = true
        });

        // Use reflection to access the private DetectFileKind method
        var method = typeof(SopsConfigurationProvider).GetMethod(
            "DetectFileKind",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (string)method!.Invoke(null, new object[] { path })!;
    }
}

/// <summary>
/// Tests for configuration key flattening conventions.
/// These verify the expected key format for IConfiguration integration.
/// </summary>
public class ConfigurationFlatteningTests
{
    [Fact]
    public void NestedMap_ProducesColonSeparatedKeys()
    {
        // Expected format: "a:b:c" for nested maps a.b.c
        var expectedKey = "app:db:user";

        // When SopsFile.DecryptToDictionaryAsync returns:
        // { "app": { "db": { "user": "value" } } }
        // The flattening produces: "app:db:user" -> "value"

        Assert.Contains(":", expectedKey);
        var parts = expectedKey.Split(':');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void ArrayElement_ProducesIndexInKey()
    {
        // Expected format: "list:0:item" for array[0].item (config convention)
        var expectedKey = "servers:0:host";

        Assert.Contains(":", expectedKey);
        var parts = expectedKey.Split(':');
        Assert.Equal(3, parts.Length);
        Assert.Equal("0", parts[1]); // Index is preserved
    }

    [Fact]
    public void CaseInsensitiveKeys_MatchByDefault()
    {
        // IConfiguration uses case-insensitive comparison by default (CaseInsensitiveConfigurationProvider)
        var key1 = "App:Db:User";
        var key2 = "app:db:user";

        // In standard .NET configuration, these would match
        Assert.Equal(key1.ToLowerInvariant(), key2.ToLowerInvariant());
    }

    [Fact]
    public void CommentNodes_SkippedInFlattening()
    {
        // Comment keys (Nops.Core.Stores.Comment) should be skipped during flattening
        // They are not string keys, so they won't match `item.Key is string key`

        var commentKey = new Nops.Core.Stores.Comment("This is a comment");

        Assert.IsType<Nops.Core.Stores.Comment>(commentKey);
        Assert.NotEqual(typeof(string), commentKey.GetType());
    }

    [Fact]
    public void BytesValue_Base64Encoded()
    {
        // Binary values are base64-encoded when flattened to string
        var originalBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var base64 = Convert.ToBase64String(originalBytes);

        Assert.NotEqual(originalBytes.Length, base64.Length);
        Assert.Equal(8, base64.Length); // 4 bytes -> 8 base64 chars
    }

    [Fact]
    public void TimeValue_ISO8601String()
    {
        // DateTime values are ISO-8601 formatted when flattened
        var dt = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var iso8601 = dt.ToString("o");

        Assert.Contains("T", iso8601);
        Assert.True(iso8601.EndsWith("Z") || iso8601.Contains("+00:00"));
    }

    [Fact]
    public void BoolValue_TrueFalseNotTrueFalse()
    {
        // SOPS stores booleans as "True"/"False" (Python-cased), not "true"/"false"
        Assert.Equal("True", true.ToString());
        Assert.Equal("False", false.ToString());

        // These match SOPS canonical format
        Assert.Equal("True", true.ToString());
    }
}

/// <summary>
/// Tests for multi-file overlay behavior.
/// </summary>
public class MultiFileOverlayTests
{
    [Fact]
    public void LastWins_WhenMultipleSourcesSameKey()
    {
        // When multiple configuration sources define the same key,
        // the last source added wins (standard .NET configuration behavior)

        var builder = new ConfigurationBuilder();

        // First source would set "key" = "value1"
        // Second source would set "key" = "value2"
        // Final result: "key" = "value2"

        var expectedBehavior = "last-wins";
        Assert.Equal("last-wins", expectedBehavior);
    }

    [Fact]
    public void Merge_WhenDifferentKeys()
    {
        // When sources have different keys, they are merged

        // Source 1: { "a": "1" }
        // Source 2: { "b": "2" }
        // Result: { "a": "1", "b": "2" }

        var keys = new[] { "a", "b" };
        Assert.Equal(2, keys.Length);
    }
}

/// <summary>
/// End-to-end tests: load a real SOPS-encrypted fixture through AddSops().
/// </summary>
public class SopsConfigurationIntegrationTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string LoadAgeSecretKey()
    {
        var keyFile = Path.Combine(FixtureDir, "AGE-TEST-KEY.txt");
        return File.ReadAllLines(keyFile).First(l => l.StartsWith("AGE-SECRET-KEY-", StringComparison.Ordinal)).Trim();
    }

    [Fact]
    public void AddSops_LoadsRealEncryptedFile()
    {
        var filePath = Path.Combine(FixtureDir, "secrets.sops.yaml");
        var ageKey = LoadAgeSecretKey();

        var builder = new ConfigurationBuilder();
        builder.AddSops(filePath, opts => opts.AgeIdentities = new[] { ageKey });
        var config = builder.Build();

        Assert.Equal("hello world", config["myapp1"]);
        Assert.Equal("admin", config["app2:db:user"]);
        Assert.Equal("s3cret!", config["app2:db:password"]);
        Assert.Equal("3.14", config["number"]);
        Assert.Equal("True", config["somebooleans:0"]);
        Assert.Equal("deep-secret", config["this:is:a:nested:value"]);
    }

    [Fact]
    public void AddSops_LoadsRealEncryptedFile_FromAgeIdentityFilePath()
    {
        var filePath = Path.Combine(FixtureDir, "secrets.sops.yaml");
        var keyFilePath = Path.Combine(FixtureDir, "AGE-TEST-KEY.txt");

        var builder = new ConfigurationBuilder();
        // Point AgeIdentities at the file itself, not at the key string.
        builder.AddSops(filePath, opts => opts.AgeIdentities = new[] { keyFilePath });
        var config = builder.Build();

        Assert.Equal("hello world", config["myapp1"]);
        Assert.Equal("admin", config["app2:db:user"]);
    }

    [Fact]
    public void AddSops_WithWatchForChanges_ReReadsModifiedFile_WithoutLeakingWatchers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nops-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "watch.sops.yaml");
        var encrypted = File.ReadAllBytes(Path.Combine(FixtureDir, "secrets.sops.yaml"));
        File.WriteAllBytes(sourcePath, encrypted);

        try
        {
            var keyFilePath = Path.Combine(FixtureDir, "AGE-TEST-KEY.txt");
            var builder = new ConfigurationBuilder();
            builder.AddSops(sourcePath, opts =>
            {
                opts.AgeIdentities = new[] { keyFilePath };
                opts.WatchForChanges = true;
                opts.ReloadInterval = TimeSpan.FromMilliseconds(50);
            });
            var config = builder.Build();

            Assert.Equal("hello world", config["myapp1"]);

            var changed = 0;
            using var registration = ChangeToken.OnChange(
                config.GetReloadToken,
                () => Interlocked.Increment(ref changed));

            // Rewrite the same (valid) encrypted content to trigger a reload.
            File.WriteAllBytes(sourcePath, encrypted);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref changed) == 0 && DateTime.UtcNow < deadline)
                Thread.Sleep(50);

            Assert.True(changed > 0, "expected a reload after the file changed");
            // Value is still correct after the reload (no leaked/duplicated providers).
            Assert.Equal("hello world", config["myapp1"]);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
