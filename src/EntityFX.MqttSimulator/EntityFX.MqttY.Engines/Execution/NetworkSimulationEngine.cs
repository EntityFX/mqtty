using System.Diagnostics;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Network;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Engines.Execution;

/// <summary>
/// Исполнитель симуляции: строит NetworkSimulator из NetworkGraphOption, выполняет фазы
/// warmup/measurement/drain с прогрессом, лимитом времени и отменой, сохраняет результаты.
/// Используется всеми движками (relay, federation, loadBalancing) для in-process исполнения.
/// </summary>
public sealed class NetworkSimulationEngine
{
    private const long ProgressEveryTicks = 1000;

    public async Task<ExperimentResult> RunAsync(
        SimulationRunOptions options,
        string? outputDirectory,
        IProgress<EngineProgress>? progress,
        TimeSpan? timeLimit,
        CancellationToken ct)
    {
        using var serviceProvider = EngineServiceProvider.Create(options.Graph);

        var ticks = options.Graph.Ticks;
        if (ticks.TickPeriod <= TimeSpan.Zero)
        {
            ticks.TickPeriod = TimeSpan.FromMilliseconds(100);
        }

        var logger = new NetworkLogger(
            scopesEnabled: false,
            simulationTickTime: ticks.TickPeriod,
            ignore: new MonitoringIgnoreOption());

        var pathFinder = serviceProvider.GetRequiredService<IPathFinder>();
        var simulator = new NetworkSimulator(pathFinder, logger, ticks, options.Graph.EnableCounters);

        try
        {
            var builder = serviceProvider.GetRequiredService<INetworkSimulatorBuilder>();
            builder.OptionsPath = string.Empty;

            var waitModeProp = typeof(NetworkSimulator).GetProperty("WaitMode",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var setter = waitModeProp?.GetSetMethod(true);
            setter?.Invoke(simulator, new object[] { true });

            // Запуск Configure на фоновом потоке, обновление на основном
            var configureTask = Task.Run(() => builder.Configure(simulator, options.Graph));
            while (!configureTask.IsCompleted)
            {
                try { simulator.Refresh(false, 0); } catch { }
                Thread.Sleep(1);
            }
            await configureTask;

            setter?.Invoke(simulator, new object[] { false });

            var stopwatch = Stopwatch.StartNew();
            var timedOut = false;
            var failed = false;
            long tick = 0;

            for (long i = 0; i < options.WarmupTicks && !timedOut && !failed; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (timeLimit.HasValue && stopwatch.Elapsed > timeLimit.Value) { timedOut = true; break; }
                failed = !simulator.RefreshWithCounters(options.Parallel, options.RefreshStrategy);
                tick++;
                ReportProgress(progress, simulator, tick);
            }

            simulator.ResetMeasurement();

            for (long i = 0; i < options.MeasurementTicks && !timedOut && !failed; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (timeLimit.HasValue && stopwatch.Elapsed > timeLimit.Value) { timedOut = true; break; }
                failed = !simulator.RefreshWithCounters(options.Parallel, options.RefreshStrategy);
                tick++;
                ReportProgress(progress, simulator, tick);
            }

            for (long i = 0; i < options.DrainTimeoutTicks && !timedOut && !failed; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (timeLimit.HasValue && stopwatch.Elapsed > timeLimit.Value) { timedOut = true; break; }
                failed = !simulator.RefreshWithCounters(options.Parallel, options.RefreshStrategy);
                tick++;
                if (simulator.IsQuiescent) break;
                ReportProgress(progress, simulator, tick);
            }

            if (outputDirectory != null)
            {
                var writer = new SimulationResultWriter(outputDirectory);
                writer.Write(simulator, options, timedOut, failed);
            }

            return new ExperimentResult
            {
                Success = !timedOut && !failed,
                ExitCode = timedOut ? 2 : failed ? 1 : 0,
                OutputDirectory = outputDirectory,
                TotalTicks = simulator.TotalTicks,
                TotalSteps = simulator.TotalSteps,
                Errors = simulator.Errors,
                Error = timedOut ? "Time limit exceeded" : failed ? "Simulation refresh failed" : null
            };
        }
        finally
        {
            simulator.Clear();
        }
    }

    private static void ReportProgress(IProgress<EngineProgress>? progress, NetworkSimulator simulator, long tick)
    {
        if (progress == null || tick % ProgressEveryTicks != 0)
        {
            return;
        }

        progress.Report(new EngineProgress
        {
            TotalTicks = tick,
            Step = simulator.TotalSteps,
            Errors = (int)simulator.Errors,
            Nodes = NodeSnapshotBuilder.Build(simulator)
        });
    }
}
