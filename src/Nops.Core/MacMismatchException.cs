namespace Nops.Core;

/// <summary>
/// Thrown when the file's MAC does not match its contents: the file was edited without sops, or it is
/// truncated or corrupt. Decryption never returns values from a file that fails this check unless
/// <see cref="SopsOptions.ThrowOnMacMismatch"/> is turned off.
/// </summary>
public sealed class MacMismatchException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public MacMismatchException(string message) : base(message) { }
}
