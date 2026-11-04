using System.Diagnostics;

namespace EntityFX.MqttY.LoadBalancing;

/// <summary>Optional wall-clock observations, kept outside scientific result artifacts.</summary>
public sealed class LoadBalancingExecutionTiming
{
    private readonly Dictionary<string, long> refreshTicks = new();
    private readonly Dictionary<string, long> refreshCalls = new();
    private readonly Dictionary<string, double> phaseSeconds = new();
    public long StopwatchFrequency => Stopwatch.Frequency;
    public double RunnerWallSeconds { get; internal set; }
    public double ExportWallSeconds { get; internal set; }
    public IReadOnlyDictionary<string, double> PhaseWallSeconds => phaseSeconds;
    public IReadOnlyDictionary<string, long> RefreshCallsByPhase => refreshCalls;
    public IReadOnlyDictionary<string, double> RefreshWallSecondsByPhase =>
        refreshTicks.ToDictionary(p => p.Key, p => (double)p.Value / Stopwatch.Frequency);
    public double RefreshWallSeconds => (double)refreshTicks.Values.Sum() / Stopwatch.Frequency;

    internal void Refresh(string phase, long elapsed)
    {
        refreshTicks[phase] = refreshTicks.GetValueOrDefault(phase) + elapsed;
        refreshCalls[phase] = refreshCalls.GetValueOrDefault(phase) + 1;
    }
    internal void CompletePhase(string phase, long elapsed) =>
        phaseSeconds[phase] = (double)elapsed / Stopwatch.Frequency;
}
