using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nops.Configuration;

var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

// Add SOPS-encrypted configuration file
builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    // Optional: configure decryption options
    opts.Optional = false;        // Throw if file missing (default)
    opts.WatchForChanges = true;  // Auto-reload on file change
    opts.ReloadInterval = TimeSpan.FromMilliseconds(250);
});

// You can also add multiple SOPS files (last-wins for overlapping keys)
// builder.Configuration.AddSops("secrets-overrides.sops.yaml", opts =>
// {
//     opts.Optional = true;  // Overrides are optional
// });

var host = builder.Build();

// Access secrets via IConfiguration
var config = host.Services.GetRequiredService<IConfiguration>();

Console.WriteLine("=== ConfigurationSample ===");
Console.WriteLine("Loaded configuration keys:");

foreach (var kvp in GetAllConfigValues(config))
{
    Console.WriteLine($"  {kvp.Key} = {MaskValue(kvp.Key, kvp.Value)}");
}

Console.WriteLine();
Console.WriteLine("To see unmasked values, set LOG_SECRETS=true environment variable.");

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOG_SECRETS")))
{
    Console.WriteLine("\n=== Unmasked Secrets ===");
    foreach (var kvp in GetAllConfigValues(config).OrderBy(k => k.Key))
    {
        Console.WriteLine($"  {kvp.Key} = {kvp.Value}");
    }
}

Console.WriteLine();
Console.WriteLine("Press any key to exit...");
Console.ReadKey();

return 0;

static IEnumerable<(string Key, string Value)> GetAllConfigValues(IConfiguration config)
{
    var results = new List<(string, string)>();

    void Walk(IConfigurationSection section, string prefix)
    {
        var key = string.IsNullOrEmpty(prefix) ? section.Key : $"{prefix}:{section.Key}";

        if (!section.GetChildren().Any())
        {
            if (section.Value != null)
                results.Add((key, section.Value));
        }
        else
        {
            foreach (var child in section.GetChildren())
            {
                Walk(child, key);
            }
        }
    }

    Walk(config.GetSection(""), "");
    return results;
}

static string MaskValue(string key, string value)
{
    var sensitiveKeywords = new[] { "password", "secret", "key", "token", "api_key" };
    var isSensitive = sensitiveKeywords.Any(kw => key.ToLower().Contains(kw));

    if (!isSensitive || string.IsNullOrEmpty(value))
        return value;

    if (value.Length <= 4)
        return new string('*', value.Length);

    return $"{value.Substring(0, 2)}{new string('*', value.Length - 2)}";
}
