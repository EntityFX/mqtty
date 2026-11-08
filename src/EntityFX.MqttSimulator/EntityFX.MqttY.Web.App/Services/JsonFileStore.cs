using System.Collections.Concurrent;
using System.Text.Json;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>CRUD-обёртка над каталогом данных с защитой от выхода за его пределы.</summary>
public sealed class JsonFileStore : IJsonDocumentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _root;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public JsonFileStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    /// <summary>True when <paramref name="path"/> resolves inside the data directory.</summary>
    public bool IsWithinRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(full, _root, comparison) ||
            full.StartsWith(Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar, comparison);
    }

    public string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("Absolute paths are not allowed.");
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(full, _root, comparison) &&
            !full.StartsWith(Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException("Path escapes data directory.");
        }

        // Existing symlink/reparse-point components cannot redirect writes outside the store.
        for (var current = full; current != null && current.Length >= _root.Length; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Symbolic links are not allowed in data paths.");
        return full;
    }

    public async Task<T?> ReadAsync<T>(string relativePath)
    {
        var path = Resolve(relativePath);
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
        return value == null ? default : PersistenceMigrations.Normalize(value);
    }

    /// <summary>Corrupt or unreadable documents are treated as missing during startup.</summary>
    public async Task<T?> TryReadAsync<T>(string relativePath)
    {
        try
        {
            return await ReadAsync<T>(relativePath);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException
            or InvalidOperationException or NotSupportedException)
        {
            return default;
        }
    }

    public async Task WriteAsync<T>(string relativePath, T value)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var gate = _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
                await stream.FlushAsync();
                // Make the rename durable as well as atomic.
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            gate.Release();
        }
    }

    public void Delete(string relativePath)
    {
        var path = Resolve(relativePath);
        var gate = _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Deletes oldest matching files, keeping at most <paramref name="keepNewest"/>.</summary>
    public int Prune(string relativePath, string pattern, int keepNewest)
    {
        if (keepNewest < 0) throw new ArgumentOutOfRangeException(nameof(keepNewest));
        var dir = Resolve(relativePath);
        if (!Directory.Exists(dir)) return 0;

        var files = Directory.GetFiles(dir, pattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        var removed = 0;
        foreach (var file in files.Skip(keepNewest))
        {
            file.Delete();
            removed++;
        }

        return removed;
    }

    public IEnumerable<string> ListFiles(string relativePath, string pattern)
    {
        var dir = Resolve(relativePath);
        if (!Directory.Exists(dir))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.GetFiles(dir, pattern).Select(path => Path.GetFileNameWithoutExtension(path)!);
    }
}
