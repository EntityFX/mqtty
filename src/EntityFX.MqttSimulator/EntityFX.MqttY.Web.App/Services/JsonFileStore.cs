using System.Text.Json;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>CRUD-обёртка над каталогом данных с защитой от выхода за его пределы.</summary>
public sealed class JsonFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _root;

    public JsonFileStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

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
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
    }

    public async Task WriteAsync<T>(string relativePath, T value)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Delete(string relativePath)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
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
