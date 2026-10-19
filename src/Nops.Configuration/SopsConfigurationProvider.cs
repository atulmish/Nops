using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Nops.Core;

namespace Nops.Configuration;

/// <summary>
/// IConfigurationProvider that loads SOPS-encrypted files and exposes them as key-value pairs.
/// Supports YAML, JSON, Dotenv, INI, and Binary formats.
/// </summary>
public class SopsConfigurationProvider : ConfigurationProvider
{
    private readonly SopsOptions _options;
    private readonly string _filePath;
    private readonly string _fileKind;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    /// <summary>
    /// Create a new SOPS configuration provider.
    /// </summary>
    public SopsConfigurationProvider(SopsOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrEmpty(options.FilePath))
            throw new ArgumentException("FilePath is required", nameof(options.FilePath));

        _filePath = Path.GetFullPath(options.FilePath);
        _fileKind = options.FileKind ?? DetectFileKind(_filePath);

        // Validate file exists unless optional
        if (!options.Optional && !File.Exists(_filePath))
        {
            throw new FileNotFoundException($"SOPS file not found: {_filePath}", _filePath);
        }

        // Set up file watching if enabled
        if (options.WatchForChanges)
        {
            SetupWatcher();
        }
    }

    /// <summary>
    /// Load and decrypt the SOPS file, populating the Data dictionary.
    /// </summary>
    public override void Load()
    {
        try
        {
            Reload();
        }
        catch (Exception ex) when (_options.FileNotFoundMode == FileNotFoundMode.WarnAndContinue || _options.Optional)
        {
            // Log warning but don't throw
            System.Diagnostics.Debug.WriteLine($"SOPS configuration load failed: {ex.Message}");
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Read, decrypt and apply the file contents to <see cref="ConfigurationProvider.Data"/>.
    /// Shared by <see cref="Load"/> and the file watcher so reloads never spin up a
    /// second provider (and thus a second watcher).
    /// </summary>
    private void Reload()
    {
        var ct = CancellationToken.None;
        var fileBytes = ReadFile();

        IReadOnlyDictionary<string, string> decryptedData;

        // Use custom decrypt callback if provided
        if (_options.DecryptCallback != null)
            decryptedData = _options.DecryptCallback(fileBytes, _fileKind, ct).GetAwaiter().GetResult();
        else
            decryptedData = DecryptWithSopsFile(fileBytes, _fileKind, ct).GetAwaiter().GetResult();

        // Build the new data off to the side and publish it with a single reference
        // assignment: ConfigurationProvider.TryGet reads Data without locking, so it must
        // never observe a cleared or half-filled dictionary during a watcher reload.
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in decryptedData)
            data[kvp.Key] = kvp.Value;

        Data = data;
    }

    /// <summary>
    /// Decrypt a SOPS-encrypted file using Nops.Core.SopsFile.
    /// Returns flattened key-value pairs suitable for IConfiguration.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> DecryptWithSopsFile(
        byte[] fileBytes, string fileKind, CancellationToken ct)
    {
        // Build master keys from options
        var masterKeys = _options.ResolveMasterKeys();

        var options = new Nops.Core.SopsOptions
        {
            FilePath = _filePath,
            // ResolveMasterKeys already turns AgeIdentities into providers unless explicit
            // MasterKeys were supplied; passing them again would try every identity twice.
            AgeIdentities = _options.MasterKeys != null ? _options.AgeIdentities : null,
            MasterKeys = masterKeys
        };

        // Decrypt to dictionary using SopsFile
        var dict = await Nops.Core.SopsFile.DecryptToDictionaryAsync(
            fileBytes, fileKind, options, ct);

        return dict;
    }

    /// <summary>
    /// Read the file with size validation.
    /// </summary>
    private byte[] ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            if (_options.FileNotFoundMode == FileNotFoundMode.Throw && !_options.Optional)
                throw new FileNotFoundException($"SOPS file not found: {_filePath}", _filePath);

            return Array.Empty<byte>();
        }

        var fileInfo = new FileInfo(_filePath);

        if (_options.MaxFileSize.HasValue && fileInfo.Length > _options.MaxFileSize.Value)
        {
            throw new InvalidOperationException(
                $"SOPS file exceeds maximum size ({fileInfo.Length} > {_options.MaxFileSize} bytes): {_filePath}");
        }

        return File.ReadAllBytes(_filePath);
    }

    /// <summary>
    /// Set up FileSystemWatcher for automatic reload.
    /// </summary>
    private void SetupWatcher()
    {
        var directory = Path.GetDirectoryName(_filePath) ?? ".";
        var fileName = Path.GetFileName(_filePath);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Changed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Handle file change events with debouncing.
    /// </summary>
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var interval = _options.ReloadInterval ?? TimeSpan.FromMilliseconds(250);

        Task.Delay(interval).ContinueWith(_ =>
        {
            try
            {
                Reload();
                OnReload();
            }
            catch
            {
                // Ignore errors during reload - the next explicit load will pick up changes
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Detect file format from extension.
    /// </summary>
    private static string DetectFileKind(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.');
        return ext switch
        {
            "yaml" or "yml" => "yaml",
            "json" => "json",
            "env" or "dotenv" => "dotenv",
            "ini" => "ini",
            "bin" => "binary",
            _ => throw new InvalidOperationException($"Unknown file extension '.{ext}' - specify FileKind explicitly")
        };
    }

    /// <summary>
    /// Dispose the watcher.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _watcher?.Dispose();
        _disposed = true;
    }
}
