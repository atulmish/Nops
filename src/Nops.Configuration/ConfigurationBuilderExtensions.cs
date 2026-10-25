using System;
using Microsoft.Extensions.Configuration;
using Nops.Configuration;

namespace Nops.Configuration;

/// <summary>
/// Extension methods for adding SOPS configuration to IConfigurationBuilder and IHostApplicationBuilder.
/// </summary>
public static class ConfigurationBuilderExtensions
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
    /// <param name="configureOptions">Callback to configure SopsConfigurationOptions.</param>
    /// <returns>The IConfigurationBuilder.</returns>
    public static IConfigurationBuilder AddSops(
        this IConfigurationBuilder configurationBuilder,
        string filePath,
        Action<SopsConfigurationOptions> configureOptions)
    {
        if (configurationBuilder == null) throw new ArgumentNullException(nameof(configurationBuilder));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        var options = new SopsConfigurationOptions { FilePath = filePath };
        configureOptions(options);

        configurationBuilder.Add(new SopsConfigurationSource(options));
        return configurationBuilder;
    }

    /// <summary>
    /// Add a SOPS-encrypted file to the configuration builder with options.
    /// </summary>
    /// <param name="configurationBuilder">The IConfigurationBuilder to add to.</param>
    /// <param name="configureOptions">Callback to configure SopsConfigurationOptions (FilePath set inside callback).</param>
    /// <returns>The IConfigurationBuilder.</returns>
    public static IConfigurationBuilder AddSops(
        this IConfigurationBuilder configurationBuilder,
        Action<SopsConfigurationOptions> configureOptions)
    {
        if (configurationBuilder == null) throw new ArgumentNullException(nameof(configurationBuilder));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        var options = new SopsConfigurationOptions();
        configureOptions(options);

        if (string.IsNullOrEmpty(options.FilePath))
            throw new ArgumentException("FilePath must be set in SopsConfigurationOptions", nameof(configureOptions));

        configurationBuilder.Add(new SopsConfigurationSource(options));
        return configurationBuilder;
    }
}

/// <summary>
/// Extension methods for the generic host builder (<c>Host.CreateApplicationBuilder</c>, <c>WebApplication.CreateBuilder</c>).
/// </summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>
    /// Add a SOPS-encrypted file to the application's configuration.
    /// </summary>
    /// <param name="builder">The IHostApplicationBuilder to add to.</param>
    /// <param name="filePath">Path to the SOPS-encrypted file.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static Microsoft.Extensions.Hosting.IHostApplicationBuilder AddSops(
        this Microsoft.Extensions.Hosting.IHostApplicationBuilder builder,
        string filePath)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        builder.Configuration.AddSops(filePath);
        return builder;
    }

    /// <summary>
    /// Add a SOPS-encrypted file to the application's configuration with options.
    /// </summary>
    /// <param name="builder">The IHostApplicationBuilder to add to.</param>
    /// <param name="filePath">Path to the SOPS-encrypted file.</param>
    /// <param name="configureOptions">Callback to configure SopsConfigurationOptions.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static Microsoft.Extensions.Hosting.IHostApplicationBuilder AddSops(
        this Microsoft.Extensions.Hosting.IHostApplicationBuilder builder,
        string filePath,
        Action<SopsConfigurationOptions> configureOptions)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        builder.Configuration.AddSops(filePath, configureOptions);
        return builder;
    }
}
