using EntityFX.MqttY.Design;
using EntityFX.MqttY.Designer.Models;

namespace EntityFX.MqttY.Designer.Services;

/// <summary>
/// Designer adapter for GraphML import. Parsing and validation live in the
/// UI-independent <see cref="GraphMlDesignReader"/>; this service only maps the
/// resulting <see cref="DesignDocument"/> to the Avalonia-facing model.
/// </summary>
public class GraphMLImporterService
{
    public async Task<NetworkDesignModel> ImportFromFileAsync(string path)
    {
        var content = await File.ReadAllTextAsync(path);
        return ImportFromString(content);
    }

    public NetworkDesignModel ImportFromString(string graphmlContent) =>
        NetworkDesignModel.FromDesignDocument(GraphMlDesignReader.Read(graphmlContent));
}
