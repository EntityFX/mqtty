using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Designer.Models;
using EntityFX.MqttY.Designer.Services;
using ReactiveUI;

namespace EntityFX.MqttY.Designer.ViewModels;

public class SimulationViewModel : ReactiveObject
{
    private readonly SimulationService _simulationService;
    private readonly NetworkEditorViewModel _networkEditor;

    private bool _isRunning;
    private bool _isPaused;
    private string _statusMessage = "Stopped";
    private long _totalTicks;
    private long _totalSteps;
    private long _errors;
    private int _countNodes;
    private bool _isBuilt;

    // State viewer collections
    private ObservableCollection<NetworkStateItem> _networkStates = new();
    private ObservableCollection<NodeStateItem> _nodeStates = new();
    private ObservableCollection<LogItem> _logItems = new();

    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            this.RaiseAndSetIfChanged(ref _isPaused, value);
            IsNotPaused = !value;
        }
    }

    private bool _isNotPaused = true;
    public bool IsNotPaused
    {
        get => _isNotPaused;
        set => this.RaiseAndSetIfChanged(ref _isNotPaused, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public long TotalTicks
    {
        get => _totalTicks;
        set => this.RaiseAndSetIfChanged(ref _totalTicks, value);
    }

    public long TotalSteps
    {
        get => _totalSteps;
        set => this.RaiseAndSetIfChanged(ref _totalSteps, value);
    }

    public long Errors
    {
        get => _errors;
        set => this.RaiseAndSetIfChanged(ref _errors, value);
    }

    public int CountNodes
    {
        get => _countNodes;
        set => this.RaiseAndSetIfChanged(ref _countNodes, value);
    }

    public bool IsBuilt
    {
        get => _isBuilt;
        set => this.RaiseAndSetIfChanged(ref _isBuilt, value);
    }

    private string _logFilePath = string.Empty;
    public string LogFilePath
    {
        get => _logFilePath;
        set => this.RaiseAndSetIfChanged(ref _logFilePath, value);
    }

    public ObservableCollection<NetworkStateItem> NetworkStates
    {
        get => _networkStates;
        set => this.RaiseAndSetIfChanged(ref _networkStates, value);
    }

    public ObservableCollection<NodeStateItem> NodeStates
    {
        get => _nodeStates;
        set => this.RaiseAndSetIfChanged(ref _nodeStates, value);
    }

    public ObservableCollection<LogItem> LogItems
    {
        get => _logItems;
        set => this.RaiseAndSetIfChanged(ref _logItems, value);
    }

    public ReactiveCommand<Unit, Unit> BuildCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> PauseCommand { get; }
    public ReactiveCommand<Unit, Unit> ResumeCommand { get; }

    public SimulationViewModel(SimulationService simulationService, NetworkEditorViewModel networkEditor)
    {
        _simulationService = simulationService;
        _networkEditor = networkEditor;

        var canStart = this.WhenAnyValue(x => x.IsBuilt, x => x.IsRunning, x => x.IsPaused,
            (built, running, paused) => built && !running && !paused);
        var canStop = this.WhenAnyValue(x => x.IsRunning, x => x.IsPaused,
            (running, paused) => running || paused);
        var canBuild = this.WhenAnyValue(x => x.IsRunning, x => x.IsPaused,
            (running, paused) => !running && !paused);
        var canPause = this.WhenAnyValue(x => x.IsRunning, x => x.IsPaused,
            (running, paused) => running && !paused);
        var canResume = this.WhenAnyValue(x => x.IsPaused);

        BuildCommand = ReactiveCommand.CreateFromTask(BuildAsync, canBuild);
        StartCommand = ReactiveCommand.CreateFromTask(StartAsync, canStart);
        StopCommand = ReactiveCommand.Create(Stop, canStop);
        PauseCommand = ReactiveCommand.Create(Pause, canPause);
        ResumeCommand = ReactiveCommand.CreateFromTask(ResumeAsync, canResume);

        // Subscribe to simulation events
        _simulationService.OnRefresh += OnSimulationRefresh;
        _simulationService.OnError += OnSimulationError;
        _simulationService.OnStopped += OnSimulationStopped;
        _simulationService.OnPaused += OnSimulationPaused;
        _simulationService.OnResumed += OnSimulationResumed;
    }

    /// <summary>
    /// Subscribes to the network simulator's monitoring logger events.
    /// Called after a successful build.
    /// </summary>
    private void SubscribeToMonitoring()
    {
        var sim = _simulationService.NetworkSimulator;
        if (sim == null) return;

        sim.Monitoring.Added += OnMonitoringItemAdded;
    }

    private void UnsubscribeFromMonitoring()
    {
        var sim = _simulationService.NetworkSimulator;
        if (sim == null) return;

        sim.Monitoring.Added -= OnMonitoringItemAdded;
    }

    private void OnMonitoringItemAdded(object? sender, NetworkLoggerItem item)
    {
        // Use Avalonia UI thread to add items
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _logItems.Add(new LogItem
            {
                Tick = item.Tick,
                Date = item.Date,
                From = item.From,
                To = item.To,
                Type = item.Type.ToString(),
                Protocol = item.Protocol,
                Message = item.Message,
                PacketSize = item.PacketSize,
                QueueLength = item.QueueLength ?? 0
            });

            // Keep last 1000 items to avoid memory issues
            if (_logItems.Count > 1000)
            {
                _logItems.RemoveAt(0);
            }
        });
    }

    private async Task BuildAsync()
    {
        var design = _networkEditor.DesignModel;
        if (design == null)
        {
            StatusMessage = "No design loaded";
            return;
        }

        StatusMessage = "Building simulation...";
        IsBuilt = false;

        var success = await _simulationService.BuildAsync(design);
        if (success)
        {
            IsBuilt = true;
            StatusMessage = "Simulation built. Ready to start.";
            UpdateCounters();
            SubscribeToMonitoring();
            LogFilePath = _simulationService.FileLog.LogFilePath;
        }
        else
        {
            StatusMessage = "Build failed. Check errors.";
        }
    }

    private async Task StartAsync()
    {
        if (!IsBuilt) return;

        StatusMessage = "Running...";
        IsRunning = true;
        IsPaused = false;
        await _simulationService.StartAsync();
    }

    private void Stop()
    {
        _simulationService.Stop();
        IsRunning = false;
        IsPaused = false;
        StatusMessage = "Stopped";
        NetworkStates.Clear();
        NodeStates.Clear();
        LogItems.Clear();
    }

    private void Pause()
    {
        _simulationService.Pause();
        // IsPaused will be set by the event handler
    }

    private async Task ResumeAsync()
    {
        await _simulationService.ResumeAsync();
        // IsPaused will be set by the event handler
    }

    private void OnSimulationRefresh(object? sender, long ticks)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateCounters();
            UpdateStateView();
        });
    }

    private void OnSimulationError(object? sender, Exception ex)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Errors = _simulationService.NetworkSimulator?.Errors ?? 0;
            StatusMessage = $"Error: {ex.Message}";
        });
    }

    private void OnSimulationStopped(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            IsPaused = false;
            StatusMessage = "Stopped";
            UpdateCounters();
        });
    }

    private void OnSimulationPaused(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsPaused = true;
            IsRunning = true;
            StatusMessage = "Paused";
            UpdateStateView();
        });
    }

    private void OnSimulationResumed(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsPaused = false;
            IsRunning = true;
            StatusMessage = "Running...";
        });
    }

    private void UpdateCounters()
    {
        var sim = _simulationService.NetworkSimulator;
        if (sim == null) return;

        TotalTicks = sim.TotalTicks;
        TotalSteps = sim.TotalSteps;
        Errors = sim.Errors;
        CountNodes = sim.CountNodes;
    }

    private void UpdateStateView()
    {
        var sim = _simulationService.NetworkSimulator;
        if (sim == null) return;

        // Update networks
        _networkStates.Clear();
        foreach (var kvp in sim.Networks)
        {
            var network = kvp.Value;
            _networkStates.Add(new NetworkStateItem
            {
                Name = kvp.Key,
                Address = network.Address,
                NodeCount = network.Nodes?.Count ?? 0,
                QueueLength = network.QueueSize
            });
        }

        // Update nodes (clients, servers, applications)
        _nodeStates.Clear();
        foreach (var kvp in sim.Clients)
        {
            var client = kvp.Value;
            _nodeStates.Add(new NodeStateItem
            {
                Name = kvp.Key,
                Address = client.Address,
                Type = "Client",
                IsConnected = client.IsConnected,
                Network = client.Network?.Name ?? ""
            });
        }
        foreach (var kvp in sim.Servers)
        {
            var server = kvp.Value;
            _nodeStates.Add(new NodeStateItem
            {
                Name = kvp.Key,
                Address = server.Address,
                Type = "Server",
                IsStarted = server.IsStarted,
                Network = server.Network?.Name ?? ""
            });
        }
        foreach (var kvp in sim.Applications)
        {
            var app = kvp.Value;
            _nodeStates.Add(new NodeStateItem
            {
                Name = kvp.Key,
                Address = app.Address,
                Type = "Application",
                IsStarted = app.IsStarted,
                Network = app.Network?.Name ?? ""
            });
        }
    }
}

public class NetworkStateItem
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int NodeCount { get; set; }
    public long QueueLength { get; set; }
}

public class NodeStateItem
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public bool IsStarted { get; set; }
    public string Network { get; set; } = string.Empty;
}

public class LogItem
{
    public long Tick { get; set; }
    public DateTimeOffset Date { get; set; }
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public uint PacketSize { get; set; }
    public long QueueLength { get; set; }
}