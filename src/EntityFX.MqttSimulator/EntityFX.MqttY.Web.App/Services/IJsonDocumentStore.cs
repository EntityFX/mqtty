namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Atomic, root-scoped JSON document store used by the web application.</summary>
public interface IJsonDocumentStore
{
    string Root { get; }

    bool IsWithinRoot(string path);

    string Resolve(string relativePath);

    Task<T?> ReadAsync<T>(string relativePath);

    Task<T?> TryReadAsync<T>(string relativePath);

    Task WriteAsync<T>(string relativePath, T value);

    void Delete(string relativePath);

    IEnumerable<string> ListFiles(string relativePath, string pattern);

    int Prune(string relativePath, string pattern, int keepNewest);
}
