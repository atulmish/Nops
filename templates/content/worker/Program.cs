using Microsoft.Extensions.Options;
using Nops.Configuration;

var builder = Host.CreateApplicationBuilder(args);

// Precedence, lowest to highest: appsettings.json, secrets.sops.yaml, environment variables, command line.
builder.Configuration
    .AddSops(Path.Combine(AppContext.BaseDirectory, "secrets.sops.yaml"), opts => opts.WatchForChanges = true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Section)
    .Validate(o => !string.IsNullOrEmpty(o.Password), "Database:Password is missing")
    .ValidateOnStart();
builder.Services.AddHostedService<Worker>();

builder.Build().Run();

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class Worker(IOptionsMonitor<DatabaseOptions> database, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // IOptionsMonitor sees a rotated secrets file without a restart. Never log the secret itself.
            var options = database.CurrentValue;
            logger.LogInformation("Connected to {Host}:{Port} as {User} (password: {Length} characters)",
                options.Host, options.Port, options.User, options.Password.Length);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
