using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Designer.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Designer.Services;

public class SimulationService : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private INetworkSimulator? _networkSimulator;
    private bool _isRunning;
    private bool _isPaused;

    public INetworkSimulator? NetworkSimulator => _networkSimulator;
    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;

    public event EventHandler<long>? OnRefresh;
    public event EventHandler<Exception>? OnError;
    public event EventHandler? OnStopped;
    public event EventHandler? OnPaused;
    public event EventHandler? OnResumed;

    public SimulationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<bool> BuildAsync(NetworkDesignModel designModel)
    {
        try
        {
            // Convert design model to NetworkGraphOption
            var graphOption = designModel.ToNetworkGraphOption();

            // Create factory options
            var factoryOptions = new NetworkGraphFactoryOption
            {
                NetworkGraphOption = graphOption,
                TicksOption = designModel.Ticks,
                EnableCounters = designModel.EnableCounters,
                MonitoringOption = new MonitoringOption
                {
                    Type = "console", // Enable logging for UI monitoring
                    ScopesEnabled = false
                },
                NetworkSimulatorBuilder = _serviceProvider.GetRequiredService<INetworkSimulatorBuilder>(),
                NetworkGraphFactory = _serviceProvider.GetRequiredService<IFactory<INetworkSimulator, NetworkGraphFactoryOption>>()
            };

            // Create the network simulator
            var graphFactory = _serviceProvider.GetRequiredService<IFactory<INetworkSimulator, NetworkGraphFactoryOption>>();
            _networkSimulator = graphFactory.Create(factoryOptions);

            // Subscribe to events
            _networkSimulator.OnRefresh += (_, ticks) => OnRefresh?.Invoke(this, ticks);
            _networkSimulator.OnError += (_, ex) => OnError?.Invoke(this, ex);

            // Configure the simulator with the graph option
            var builder = _serviceProvider.GetRequiredService<INetworkSimulatorBuilder>();
            builder.OptionsPath = string.Empty;
            builder.Configure(_networkSimulator, graphOption);

            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            return false;
        }
    }

    public async Task StartAsync()
    {
        if (_networkSimulator == null || _isRunning) return;

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
    }
}