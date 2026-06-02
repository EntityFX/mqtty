using System.Text.Json;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Designer.Models;

namespace EntityFX.MqttY.Designer.Services;

public class ConfigurationService : IConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<NetworkDesignModel> LoadFromFileAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return await DeserializeFromJson(json);
    }

    public async Task SaveToFileAsync(NetworkDesignModel model, string path)
    {
        var json = await SerializeToJson(model);
        await File.WriteAllTextAsync(path, json);
    }

    public Task<string> SerializeToJson(NetworkDesignModel model)
    {
        var option = model.ToNetworkGraphOption();
        var json = JsonSerializer.Serialize(option, JsonOptions);
        return Task.FromResult(json);
    }

    public Task<NetworkDesignModel> DeserializeFromJson(string json)
    {
        var option = JsonSerializer.Deserialize<NetworkGraphOption>(json, JsonOptions);
        if (option == null)
        {
            throw new InvalidOperationException("Failed to deserialize network configuration.");
        }
        var model = NetworkDesignModel.FromNetworkGraphOption(option);
        return Task.FromResult(model);
    }
}