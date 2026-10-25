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
public class SopsConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly SopsConfigurationOptions _options;
    private readonly string _filePath;
    private readonly string _fileKind;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pendingReload;
    private readonly object _reloadLock = new();
    private bool _disposed;

    /// <summary>
    /// Create a new SOPS configuration provider.
    /// </summary>
    public SopsConfigurationProvider(SopsConfigurationOptions options)
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
    /// A missing file is tolerated only when <see cref="SopsConfigurationOptions.Optional"/> is set;
    /// every other failure throws.
    /// </summary>
    public override void Load()
    {
        if (_options.Optional && !File.Exists(_filePath))
        {
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        Reload();
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
            // ResolveMasterKeys already turns AgeIdentities/PgpKeys into providers unless
            // explicit MasterKeys were supplied; in that case let Nops.Core add them on top.
            AgeIdentities = _options.MasterKeys != null ? _options.AgeIdentities : null,
            PgpKeys = _options.MasterKeys != null ? _options.PgpKeys : null,
            MasterKeys = masterKeys,
            UseSopsEnvironment = _options.UseSopsEnvironment,
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
            throw new FileNotFoundException($"SOPS file not found: {_filePath}", _filePath);

        var fileInfo = new FileInfo(_filePath);

        if (_options.MaxFileSize.HasValue && fileInfo.Length > _options.MaxFileSize.Value)
        {
            throw new InvalidOperationException(
                $"SOPS file exceeds maximum size ({fileInfo.Length} > {_options.MaxFileSize} bytes): {_filePath}");
        }

        return File.ReadAllBytes(_filePath);
    }

    /// <summary>
    /// Set up FileSystemWatcher for automatic reload. The directory is watched (not just the file) so that
    /// atomic replacements are seen: editors that write a temp file and rename it, and Kubernetes Secret and
    /// ConfigMap volumes, which swap a <c>..data</c> symlink instead of rewriting the file.
    /// </summary>
    private void SetupWatcher()
    {
        var directory = Path.GetDirectoryName(_filePath) ?? ".";

        _watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                | NotifyFilters.DirectoryName | NotifyFilters.CreationTime
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Handle file change events. Bursts of events collapse into one reload after the debounce interval.
    /// </summary>
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Our file, or the symlink a Kubernetes volume swaps to publish an update.
        if (e.Name != Path.GetFileName(_filePath) && e.Name != "..data")
            return;

        var interval = _options.ReloadInterval ?? TimeSpan.FromMilliseconds(250);
        CancellationToken token;
        lock (_reloadLock)
        {
            if (_disposed)
                return;
            _pendingReload?.Cancel();
            _pendingReload?.Dispose();
            _pendingReload = new CancellationTokenSource();
            token = _pendingReload.Token;
        }

        Task.Delay(interval, token).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;

            try
            {
                Reload();
                OnReload();
            }
            catch (Exception ex)
            {
                // Keep the previous values: a half-written or undecryptable file must not wipe the config.
                try { _options.OnReloadError?.Invoke(ex); } catch { /* a faulty callback must not crash the process */ }
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
    /// Stop watching the file.
    /// </summary>
    public void Dispose()
    {
        lock (_reloadLock)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingReload?.Cancel();
            _pendingReload?.Dispose();
            _pendingReload = null;
        }

        _watcher?.Dispose();
    }
}
