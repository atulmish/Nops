// ASP.NET Core minimal API: SOPS secrets as a configuration source, consumed through options.
//
// Run:  dotnet run   then   curl http://localhost:5080/
// launchSettings.json points SOPS_AGE_KEY_FILE at the TEST-ONLY key used for the sample file.
using Microsoft.Extensions.Options;
using Nops.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Layered like any other source: values here override appsettings.json, and
// environment variables / command line added later still override these.
builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    opts.Optional = builder.Environment.IsDevelopment(); // missing file is fine locally
    opts.WatchForChanges = true;                          // IOptionsMonitor sees rotations
});

builder.Services.AddOptions<ApiOptions>().BindConfiguration(ApiOptions.Section);

var app = builder.Build();

// Never return secret values from an endpoint - report what is configured instead.
app.MapGet("/", (IOptionsMonitor<ApiOptions> api, IConfiguration config) => new
{
    api.CurrentValue.BaseUrl,
    ApiKeyConfigured = !string.IsNullOrEmpty(api.CurrentValue.Key),
    DatabaseHost = config["Database:Host"],
});

app.Run();

public sealed class ApiOptions
{
    public const string Section = "Api";

    public string BaseUrl { get; set; } = "";
    public string Key { get; set; } = "";
}
