// The layout most apps want: plaintext appsettings.json for everything reviewable, plus ONE
// SOPS file that holds only the secrets. Later sources win, so the order below is the
// precedence: appsettings.json < secrets.sops.yaml < environment variables < command line.
//
// Run:  dotnet run            (launchSettings.json points SOPS_AGE_KEY_FILE at the TEST-ONLY key)
// Try:  Database__Host=other.internal dotnet run     (an environment variable beats both files)
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Nops.Configuration;

var builder = Host.CreateApplicationBuilder(args);

// Host.CreateApplicationBuilder has already added appsettings.json, environment variables and
// the command line. Add the secrets file, then re-add the last two so they still win.
builder.Configuration
    .AddSops("secrets.sops.yaml")
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// ConfigurationManager is also an IConfigurationRoot, which exposes the providers in order.
IConfigurationRoot config = builder.Configuration;

string[] keys = ["Database:Host", "Database:Port", "Database:User", "Database:Password", "Api:BaseUrl", "Api:Key"];
string[] secrets = ["Database:Password", "Api:Key"];

foreach (var key in keys)
{
    // Providers are consulted last-to-first, so the first one that has the key supplied the value.
    var source = config.Providers.Reverse().FirstOrDefault(p => p.TryGet(key, out _));
    var value = config[key];
    var shown = secrets.Contains(key) ? $"******** ({value?.Length ?? 0} chars)" : value;
    Console.WriteLine($"{key,-20} = {shown,-32} from {Describe(source)}");
}

static string Describe(IConfigurationProvider? provider) => provider switch
{
    null => "(not set)",
    SopsConfigurationProvider => "secrets.sops.yaml (SOPS)",
    _ => provider.GetType().Name.Replace("ConfigurationProvider", ""),
};
