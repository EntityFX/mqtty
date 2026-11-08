namespace EntityFX.MqttY.Contracts.Options
{
    /// <summary>Serializable graph position (avoids a UI-framework dependency in contracts).</summary>
    public sealed class GraphPositionOption
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// Optional editor layout persisted with a topology. Older files simply omit
    /// it; the designer then falls back to its calculated layout.
    /// </summary>
    public sealed class GraphLayoutOption
    {
        public Dictionary<string, GraphPositionOption> NetworkPositions { get; set; } = new();
        public Dictionary<string, GraphPositionOption> NodePositions { get; set; } = new();
        public double ZoomLevel { get; set; } = 1.0;
    }
}
