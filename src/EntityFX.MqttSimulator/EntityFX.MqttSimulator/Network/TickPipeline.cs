namespace EntityFX.MqttY.Network;

/// <summary>One stage of the refresh tick.</summary>
internal delegate void TickStage(in TickContext context);

/// <summary>
/// Explicit stage order for a refresh tick:
/// publish the model clock/counters, refresh the topology, then complete the
/// tick (advance the clock and publish wall/virtual times). Keeping the stages
/// separate makes the order testable and removes the four duplicated refresh
/// bodies that used to live in the simulator.
/// </summary>
internal sealed class TickPipeline
{
    private readonly TickStage _publishClock;
    private readonly TickStage _refreshTopology;
    private readonly TickStage _complete;

    public TickPipeline(TickStage publishClock, TickStage refreshTopology, TickStage complete)
    {
        _publishClock = publishClock;
        _refreshTopology = refreshTopology;
        _complete = complete;
    }

    public void Run(in TickContext context)
    {
        _publishClock(in context);
        _refreshTopology(in context);
        _complete(in context);
    }
}
