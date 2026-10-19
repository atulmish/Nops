using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nops.Core.Keys;
using Xunit;

namespace Nops.Core.Tests.Keys;

public class AgeIdentityLoaderTests
{
    private const string KeyA = "AGE-SECRET-KEY-1AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string KeyB = "AGE-SECRET-KEY-1BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void SopsEnvironment_ReadsKeyKeyFileAndDefaultFile()
    {
        var dir = Directory.CreateTempSubdirectory("nops-age-");
        try
        {
            var keyFile = Path.Combine(dir.FullName, "explicit.txt");
            File.WriteAllText(keyFile, $"# created: test\n{KeyB}\n");

            var defaultFile = Path.Combine(dir.FullName, "sops", "age", "keys.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(defaultFile)!);
            File.WriteAllText(defaultFile, KeyA + "\n");

            var env = new Dictionary<string, string?>
            {
                ["SOPS_AGE_KEY"] = $"# comment\n{KeyA}",
                ["SOPS_AGE_KEY_FILE"] = keyFile,
                ["XDG_CONFIG_HOME"] = dir.FullName,
            };

            var keys = AgeIdentityLoader.LoadFromSopsEnvironment(
                name => env.GetValueOrDefault(name), File.Exists).ToList();

            Assert.Equal(new[] { KeyA, KeyB, KeyA }, keys);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void SopsEnvironment_NothingConfigured_ReturnsNothing()
    {
        var keys = AgeIdentityLoader.LoadFromSopsEnvironment(
            name => name == "XDG_CONFIG_HOME" ? "/nonexistent-nops-dir" : null, File.Exists);

        Assert.Empty(keys);
    }
}
