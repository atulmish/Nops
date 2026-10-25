namespace Nops.Core;

/// <summary>
/// Thrown when the file or one of its encrypted values fails authentication: the file was edited without sops,
/// or it is truncated or corrupt. Decryption never returns values from a file that fails this check unless
/// <see cref="SopsOptions.ThrowOnMacMismatch"/> is turned off.
/// </summary>
public sealed class MacMismatchException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public MacMismatchException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public MacMismatchException(string message, Exception innerException) : base(message, innerException) { }
}
