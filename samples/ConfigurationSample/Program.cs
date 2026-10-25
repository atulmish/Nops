// Generic host / console / worker usage: SOPS secrets bound to typed options.
//
// Run:  dotnet run            (launchSettings.json points SOPS_AGE_KEY_FILE at the TEST-ONLY key)
// Or:   SOPS_AGE_KEY_FILE=/path/to/keys.txt dotnet run --no-launch-profile
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nops.Configuration;

var builder = Host.CreateApplicationBuilder(args);

// Age keys are found the same way the sops CLI finds them (SOPS_AGE_KEY,
// SOPS_AGE_KEY_FILE, ~/.config/sops/age/keys.txt), so no key code is needed here.
builder.Configuration.AddSops("secrets.sops.yaml", opts => opts.WatchForChanges = true);

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

using var host = builder.Build();

var db = host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
Console.WriteLine($"Database: {db.Host}:{db.Port}");
Console.WriteLine($"Password loaded: {!string.IsNullOrEmpty(db.Password)} ({db.Password.Length} chars)");

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    [Required] public string Host { get; set; } = "";
    [Range(1, 65535)] public int Port { get; set; }
    [Required] public string Password { get; set; } = "";
}
