using Avalonia;
using EntityFX.MqttY.Designer.Models;

namespace EntityFX.MqttY.Designer.Services;

public interface IGraphLayoutService
{
    GraphLayoutModel CalculateLayout(NetworkDesignModel model);
    GraphLayoutModel ApplyForceDirected(NetworkDesignModel model, GraphLayoutModel current);
    GraphLayoutModel CalculateCircularLayout(NetworkDesignModel model);
    GraphLayoutModel CalculateForceDirectedLayout(NetworkDesignModel model);
    GraphLayoutModel CalculateGridLayout(NetworkDesignModel model);
    GraphLayoutModel CalculateHierarchicalLayout(NetworkDesignModel model);
}