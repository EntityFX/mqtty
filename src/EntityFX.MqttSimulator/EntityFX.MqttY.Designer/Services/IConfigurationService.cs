using EntityFX.MqttY.Designer.Models;

namespace EntityFX.MqttY.Designer.Services;

public interface IConfigurationService
{
    Task<NetworkDesignModel> LoadFromFileAsync(string path);
    Task SaveToFileAsync(NetworkDesignModel model, string path);
    Task<string> SerializeToJson(NetworkDesignModel model);
    Task<NetworkDesignModel> DeserializeFromJson(string json);
}