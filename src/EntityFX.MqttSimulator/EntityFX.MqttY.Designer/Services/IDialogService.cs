using EntityFX.MqttY.Designer.Models;

namespace EntityFX.MqttY.Designer.Services;

public interface IDialogService
{
    Task<bool> ShowNodeEditorAsync(NodeDesignModel node, List<string> availableNetworks);
    Task<bool> ShowNetworkTypeEditorAsync(NetworkTypeModel networkType);
    Task<bool> ShowNetworkEditorAsync(NetworkNodeDesignModel network, List<string> availableNetworkTypes);
}