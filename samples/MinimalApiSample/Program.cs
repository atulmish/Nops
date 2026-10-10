using Microsoft.Extensions.Configuration;
using Nops.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add SOPS-encrypted configuration
builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    opts.Optional = true; // Don't fail if file missing in dev
});

var app = builder.Build();

// Dev-only endpoint: expose decrypted secrets
if (app.Environment.IsDevelopment())
{
    app.MapGet("/secret", (IConfiguration config) =>
    {
        var results = new List<(string Key, string Value)>();

        foreach (var section in config.GetSection("").GetChildren())
        {
            Flatten(section, "", results);
        }

        return Results.Ok(results.OrderBy(r => r.Key).ToDictionary(r => r.Key, r => r.Value));
    });

    app.MapGet("/", () =>
        """
        MinimalApiSample — Nops Configuration Demo
        
        Endpoints:
          GET /       — This help text
          GET /secret — Decrypted secrets (dev only)
        
        To run with a SOPS-encrypted file:
          1. Place secrets.sops.yaml in the output directory
          2. Run: dotnet run
          3. Visit http://localhost:5000/secret
        """);
}
else
{
    app.MapGet("/", () =>
        "MinimalApiSample is running in production mode. Secrets are loaded via environment variables or other configuration sources.");
}

app.Run();

static void Flatten(IConfigurationSection section, string prefix, List<(string Key, string Value)> results)
{
    var key = string.IsNullOrEmpty(prefix) ? section.Key : $"{prefix}:{section.Key}";

    if (!section.GetChildren().Any())
    {
        results.Add((key, section.Value ?? ""));
    }
    else
    {
        foreach (var child in section.GetChildren())
        {
            Flatten(child, key, results);
        }
    }
}
