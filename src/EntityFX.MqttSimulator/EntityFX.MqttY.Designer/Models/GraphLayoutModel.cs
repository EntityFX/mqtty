using Avalonia;

namespace EntityFX.MqttY.Designer.Models;

/// <summary>
/// Stores positions of graph elements on the canvas for layout persistence.
/// </summary>
public class GraphLayoutModel
{
    public Dictionary<string, Point> NetworkPositions { get; set; } = new();
    public Dictionary<string, Point> NodePositions { get; set; } = new();
    public double ZoomLevel { get; set; } = 1.0;
    public Point Offset { get; set; }
}