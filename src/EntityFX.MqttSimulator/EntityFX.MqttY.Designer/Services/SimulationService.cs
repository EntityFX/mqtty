using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Designer.Models;
using EntityFX.MqttY.Network;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Designer.Services;

public class SimulationService : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FileLogService _fileLogService;
    private INetworkSimulator? _networkSimulator;
    private INetworkLogger? _logger;
    private bool _isRunning;
    private bool _isPaused;

    public INetworkSimulator? NetworkSimulator => _networkSimulator;
    public INetworkLogger? Logger => _logger;
    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public FileLogService FileLog => _fileLogService;

    public event EventHandler<long>? OnRefresh;
    public event EventHandler<Exception>? OnError;
    public event EventHandler? OnStopped;
    public event EventHandler? OnPaused;
    public event EventHandler? OnResumed;

    public SimulationService(IServiceProvider serviceProvider, FileLogService fileLogService)
    {
        _serviceProvider = serviceProvider;
        _fileLogService = fileLogService;
    }

    public async Task<bool> BuildAsync(NetworkDesignModel designModel)
    {
        try
        {
            // Convert design model to NetworkGraphOption
            var graphOption = designModel.ToNetworkGraphOption();

            // Ensure TicksOptions has a valid TickPeriod to avoid division-by-zero
            var ticks = designModel.Ticks;
            if (ticks.TickPeriod <= TimeSpan.Zero)
            {
                ticks.TickPeriod = TimeSpan.FromMilliseconds(100);
            }

            // Create NetworkLogger directly (not via factory) to avoid ConsoleNetworkLoggerProvider
            // which crashes in GUI apps without a console handle.
            _logger = new NetworkLogger(
                scopesEnabled: false,
                simulationTickTime: ticks.TickPeriod,
                ignore: new MonitoringIgnoreOption());

            // Create the NetworkSimulator directly
            var pathFinder = _serviceProvider.GetRequiredService<IPathFinder>();
            _networkSimulator = new NetworkSimulator(
                pathFinder, _logger, ticks, designModel.EnableCounters);

            // Subscribe to events
            _networkSimulator.OnRefresh += (_, t) => OnRefresh?.Invoke(this, t);
            _networkSimulator.OnError += (_, ex) => OnError?.Invoke(this, ex);

            // Configure the simulator with the graph option
            var builder = _serviceProvider.GetRequiredService<INetworkSimulatorBuilder>();
            builder.OptionsPath = string.Empty;
            builder.Configure(_networkSimulator, graphOption);

            // Start file logging
            _fileLogService.Start(_logger);

            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            return false;
        }
    }

    public async Task StartAsync(SimulationRunMode mode = SimulationRunMode.RealTime, double multiplier = 1.0)
    {
        if (_networkSimulator == null || _isRunning) return;

        if (_networkSimulator is NetworkSimulator concreteSim)
        {
            concreteSim.RunMode = mode;
            concreteSim.SpeedMultiplier = multiplier;
        }

        _isRunning = true;
        _isPaused = false;
        await _networkSimulator.StartPeriodicRefreshAsync();
    }

    public void Stop()
    {
        if (_networkSimulator == null || !_isRunning) return;

        _networkSimulator.StopPeriodicRefresh();
        _isRunning = false;
        _isPaused = false;

        if (_logger != null)
        {
            _fileLogService.Stop(_logger);
        }

        OnStopped?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (_networkSimulator == null || !_isRunning || _isPaused) return;

        _networkSimulator.StopPeriodicRefresh();
        _isPaused = true;
        OnPaused?.Invoke(this, EventArgs.Empty);
    }

    public async Task ResumeAsync()
    {
        if (_networkSimulator == null || !_isRunning || !_isPaused) return;

        _isPaused = false;
        await _networkSimulator.StartPeriodicRefreshAsync();
        OnResumed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Stop();
        _networkSimulator?.Clear();
        _networkSimulator = null;
        _fileLogService.Dispose();
    }
}