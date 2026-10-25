namespace Nops.Core;

/// <summary>
/// Thrown when the <c>sops</c> metadata block of a file is missing or structurally invalid.
/// </summary>
public sealed class SopsMetadataException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public SopsMetadataException(string message) : base(message) { }
}
