using System;
using Microsoft.Extensions.Configuration;
using Nops.Configuration;

namespace Nops.Configuration;

/// <summary>
/// Extension methods for adding SOPS configuration to IConfigurationBuilder and IHostApplicationBuilder.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add a SOPS-encrypted file to the configuration builder.
    /// </summary>
    /// <param name="configurationBuilder">The IConfigurationBuilder to add to.</param>
    /// <param name="filePath">Path to the SOPS-encrypted file.</param>
    /// <returns>The IConfigurationBuilder.</returns>
    public static IConfigurationBuilder AddSops(
        this IConfigurationBuilder configurationBuilder,
        string filePath)
    {
        return configurationBuilder.AddSops(filePath, _ => { });
    }

    /// <summary>
    /// Add a SOPS-encrypted file to the configuration builder with options.
    /// </summary>
    /// <param name="configurationBuilder">The IConfigurationBuilder to add to.</param>
    /// <param name="filePath">Path to the SOPS-encrypted file.</param>
    /// <param name="configureOptions">Callback to configure SopsOptions.</param>
    /// <returns>The IConfigurationBuilder.</returns>
    public static IConfigurationBuilder AddSops(
        this IConfigurationBuilder configurationBuilder,
        string filePath,
        Action<SopsOptions> configureOptions)
    {
        if (configurationBuilder == null) throw new ArgumentNullException(nameof(configurationBuilder));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        var options = new SopsOptions { FilePath = filePath };
        configureOptions(options);

        configurationBuilder.Add(new SopsConfigurationSource(options));
        return configurationBuilder;
    }

    /// <summary>
    /// Add a SOPS-encrypted file to the configuration builder with options.
    /// </summary>
    /// <param name="configurationBuilder">The IConfigurationBuilder to add to.</param>
    /// <param name="configureOptions">Callback to configure SopsOptions (FilePath set inside callback).</param>
    /// <returns>The IConfigurationBuilder.</returns>
    public static IConfigurationBuilder AddSops(
        this IConfigurationBuilder configurationBuilder,
        Action<SopsOptions> configureOptions)
    {
        if (configurationBuilder == null) throw new ArgumentNullException(nameof(configurationBuilder));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        var options = new SopsOptions();
        configureOptions(options);

        if (string.IsNullOrEmpty(options.FilePath))
            throw new ArgumentException("FilePath must be set in SopsOptions", nameof(configureOptions));

        configurationBuilder.Add(new SopsConfigurationSource(options));
        return configurationBuilder;
    }
}

/// <summary>
/// Extension methods for ASP.NET Core host builder.
/// </summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>
    /// Add a SOPS-encrypted file to the application's configuration.
    /// </summary>
    /// <param name="builder">The IHostApplicationBuilder to add to.</param>
    /// <param name="filePath">Path to the SOPS-encrypted file.</param>
    /// <param name="name">Optional name for the configuration section.</param>
    public static void AddSops(
        this Microsoft.Extensions.Hosting.IHostApplicationBuilder builder,
        string filePath,
        string? name = null)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        builder.Configuration.AddSops(filePath);
    }

    /// <summary>
    /// Add a SOPS-encrypted file to the application's configuration with options.
    /// </summary>
    /// <param name="builder">The IHostApplicationBuilder to add to.</param>
    /// <param name="filePath">Path to the SOPS-encrypted file.</param>
    /// <param name="configureOptions">Callback to configure SopsOptions.</param>
    /// <param name="name">Optional name for the configuration section.</param>
    public static void AddSops(
        this Microsoft.Extensions.Hosting.IHostApplicationBuilder builder,
        string filePath,
        Action<SopsOptions> configureOptions,
        string? name = null)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        builder.Configuration.AddSops(filePath, configureOptions);
    }
}
