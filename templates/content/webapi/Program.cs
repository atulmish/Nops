using Microsoft.Extensions.Options;
using Nops.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Precedence, lowest to highest: appsettings.json, secrets.sops.yaml, environment variables, command line.
// The key is found the way the sops CLI finds it: SOPS_AGE_KEY, SOPS_AGE_KEY_FILE, then the default key file.
builder.Configuration
    .AddSops(Path.Combine(AppContext.BaseDirectory, "secrets.sops.yaml"))
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Section)
    .Validate(o => !string.IsNullOrEmpty(o.Password), "Database:Password is missing")
    .ValidateOnStart();

var app = builder.Build();

// Report what is configured, never the secret values themselves.
app.MapGet("/", (IOptions<DatabaseOptions> database, IConfiguration configuration) => new
{
    DatabaseHost = database.Value.Host,
    DatabasePasswordConfigured = !string.IsNullOrEmpty(database.Value.Password),
    ApiBaseUrl = configuration["Api:BaseUrl"],
    ApiKeyConfigured = !string.IsNullOrEmpty(configuration["Api:Key"]),
});

app.MapGet("/healthz", () => Results.Ok());

app.Run();

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
}
