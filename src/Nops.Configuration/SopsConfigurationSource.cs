using Microsoft.Extensions.Configuration;

namespace Nops.Configuration;

/// <summary>
/// IConfigurationSource that creates SopsConfigurationProvider instances.
/// Used with IConfigurationBuilder.AddSops().
/// </summary>
public class SopsConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// The SOPS options used to configure the provider.
    /// </summary>
    public SopsConfigurationOptions Options { get; }

    /// <summary>
    /// Create a new SOPS configuration source.
    /// </summary>
    public SopsConfigurationSource(SopsConfigurationOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Build the SopsConfigurationProvider from this source.
    /// </summary>
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SopsConfigurationProvider(Options);
    }
}
