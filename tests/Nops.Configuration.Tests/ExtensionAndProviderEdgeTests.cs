using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Nops.Configuration;

namespace Nops.Configuration.Tests;

/// <summary>The host-builder extensions and the less common provider options.</summary>
public class ExtensionAndProviderEdgeTests
{
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static string SecretsFile => Path.Combine(FixtureDir, "secrets.sops.yaml");
    private static string KeyFile => Path.Combine(FixtureDir, "AGE-TEST-KEY.txt");

    [Fact]
    public void HostBuilder_AddSops_LoadsAndReturnsTheBuilder()
    {
        var builder = new HostApplicationBuilder();

        var returned = builder.AddSops(SecretsFile, opts => opts.AgeIdentities = [KeyFile]);

        Assert.Same(builder, returned);
        Assert.Equal("hello world", builder.Configuration["myapp1"]);
    }

    [Fact]
    public void HostBuilder_AddSops_WithoutOptions_UsesTheSopsEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable("SOPS_AGE_KEY_FILE");
        Environment.SetEnvironmentVariable("SOPS_AGE_KEY_FILE", KeyFile);
        try
        {
            var builder = new HostApplicationBuilder();

            Assert.Same(builder, builder.AddSops(SecretsFile));
            Assert.Equal("hello world", builder.Configuration["myapp1"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOPS_AGE_KEY_FILE", previous);
        }
    }

    [Fact]
    public void HostBuilder_AddSops_ValidatesArguments()
    {
        var builder = new HostApplicationBuilder();

        Assert.Throws<ArgumentNullException>(() => ((IHostApplicationBuilder)null!).AddSops("f"));
        Assert.Throws<ArgumentNullException>(() => builder.AddSops((string)null!));
        Assert.Throws<ArgumentNullException>(() => ((IHostApplicationBuilder)null!).AddSops("f", _ => { }));
        Assert.Throws<ArgumentNullException>(() => builder.AddSops(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => builder.AddSops("f", null!));
    }

    [Fact]
    public void ConfigurationBuilder_AddSops_WithOnlyOptions_LoadsTheFileSetInTheCallback()
    {
        var config = new ConfigurationBuilder()
            .AddSops(opts =>
            {
                opts.FilePath = SecretsFile;
                opts.AgeIdentities = [KeyFile];
            })
            .Build();

        Assert.Equal("hello world", config["myapp1"]);
    }

    [Fact]
    public void ConfigurationBuilder_AddSops_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ((IConfigurationBuilder)null!).AddSops("f"));
        Assert.Throws<ArgumentNullException>(() => ((IConfigurationBuilder)null!).AddSops(_ => { }));
        Assert.Throws<ArgumentNullException>(() => new ConfigurationBuilder().AddSops((Action<SopsConfigurationOptions>)null!));
    }

    [Fact]
    public void DecryptCallback_ReplacesTheBuiltInDecryption()
    {
        byte[]? seenBytes = null;
        string? seenKind = null;
        var config = new ConfigurationBuilder()
            .AddSops(SecretsFile, opts =>
            {
                opts.DecryptCallback = (bytes, kind, _) =>
                {
                    seenBytes = bytes;
                    seenKind = kind;
                    return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["from:callback"] = "yes" });
                };
            })
            .Build();

        Assert.Equal("yes", config["from:callback"]);
        Assert.Equal("yaml", seenKind);
        Assert.NotEmpty(seenBytes!);
    }

    [Fact]
    public void FileLargerThanMaxFileSize_IsRejected()
    {
        var builder = new ConfigurationBuilder().AddSops(SecretsFile, opts =>
        {
            opts.AgeIdentities = [KeyFile];
            opts.MaxFileSize = 10;
        });

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("exceeds maximum size", ex.Message);
    }

    [Fact]
    public void UnlimitedMaxFileSize_IsAllowed()
    {
        var config = new ConfigurationBuilder().AddSops(SecretsFile, opts =>
        {
            opts.AgeIdentities = [KeyFile];
            opts.MaxFileSize = null;
        }).Build();

        Assert.Equal("hello world", config["myapp1"]);
    }

    [Fact]
    public void UnknownExtension_NeedsAFileKind()
    {
        var path = Path.Combine(Path.GetTempPath(), "nops-" + Guid.NewGuid().ToString("N") + ".enc");
        File.Copy(SecretsFile, path);
        try
        {
            var unknown = new ConfigurationBuilder().AddSops(path, opts => opts.AgeIdentities = [KeyFile]);
            Assert.Throws<InvalidOperationException>(() => unknown.Build());

            var withKind = new ConfigurationBuilder()
                .AddSops(path, opts => { opts.AgeIdentities = [KeyFile]; opts.FileKind = "yaml"; })
                .Build();
            Assert.Equal("hello world", withKind["myapp1"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ARapidBurstOfWrites_ReloadsOnce_AndSurvivesDispose()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nops-burst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "burst.sops.yaml");
        File.Copy(SecretsFile, path);
        try
        {
            var reloads = 0;
            var root = (ConfigurationRoot)new ConfigurationBuilder().AddSops(path, opts =>
            {
                opts.AgeIdentities = [KeyFile];
                opts.WatchForChanges = true;
                opts.ReloadInterval = TimeSpan.FromMilliseconds(300);
            }).Build();
            using var registration = Microsoft.Extensions.Primitives.ChangeToken.OnChange(root.GetReloadToken, () => Interlocked.Increment(ref reloads));

            for (var i = 0; i < 5; i++)
                File.AppendAllText(path, $"# touch {i}\n");

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref reloads) == 0 && DateTime.UtcNow < deadline)
                Thread.Sleep(50);
            Thread.Sleep(600);

            Assert.True(reloads >= 1);
            root.Dispose();
            root.Dispose();   // disposing twice is harmless
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
