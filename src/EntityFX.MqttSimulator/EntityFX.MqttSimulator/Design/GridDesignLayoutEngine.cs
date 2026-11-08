using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Design;

public interface IDesignLayoutEngine
{
    DesignDocument Apply(DesignDocument document);
}

/// <summary>
/// Deterministic layout without UI dependencies: networks in a row, nodes in a
/// grid below them. Persisted positions always win over the computed layout.
/// </summary>
public sealed class GridDesignLayoutEngine : IDesignLayoutEngine
{
    private const double NetworkSpacing = 220;
    private const double NodeSpacingX = 180;
    private const double NodeSpacingY = 140;
    private const int NodesPerRow = 8;

    public DesignDocument Apply(DesignDocument document)
    {
        var layout = document.Layout ?? new GraphLayoutOption();
        var networkPositions = new Dictionary<string, GraphPositionOption>(layout.NetworkPositions);
        var nodePositions = new Dictionary<string, GraphPositionOption>(layout.NodePositions);

        for (var i = 0; i < document.Networks.Count; i++)
        {
            var network = document.Networks[i];
            networkPositions.TryAdd(network.Name, new GraphPositionOption
            {
                X = i * NetworkSpacing,
                Y = 0
            });
        }

        for (var i = 0; i < document.Nodes.Count; i++)
        {
            var node = document.Nodes[i];
            nodePositions.TryAdd(node.Name, new GraphPositionOption
            {
                X = i % NodesPerRow * NodeSpacingX,
                Y = NodeSpacingY + i / NodesPerRow * NodeSpacingY
            });
        }

        return document with
        {
            Layout = new GraphLayoutOption
            {
                NetworkPositions = networkPositions,
                NodePositions = nodePositions,
                ZoomLevel = layout.ZoomLevel
            }
        };
    }
}
