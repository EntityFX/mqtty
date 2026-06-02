using System.Windows.Input;
using EntityFX.MqttY.Designer.Models;
using EntityFX.MqttY.Designer.Services;
using ReactiveUI;

namespace EntityFX.MqttY.Designer.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    private readonly IConfigurationService _configurationService;
    private readonly GraphMLImporterService _graphMLImporter;
    private readonly NetworkEditorViewModel _networkEditor;
    private readonly SimulationViewModel _simulation;

    private int _selectedTab;
    private string _title = "MqttY Designer";
    private string? _currentFilePath;
    private NetworkDesignModel? _currentDesign;

    public int SelectedTab
    {
        get => _selectedTab;
        set => this.RaiseAndSetIfChanged(ref _selectedTab, value);
    }

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentFilePath, value);
            UpdateTitle();
        }
    }

    public NetworkDesignModel? CurrentDesign
    {
        get => _currentDesign;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentDesign, value);
            if (_networkEditor != null)
                _networkEditor.DesignModel = value;
        }
    }

    public NetworkEditorViewModel NetworkEditor => _networkEditor;

    public SimulationViewModel Simulation => _simulation;

    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ImportGraphMLCommand { get; }

    public MainWindowViewModel(
        IConfigurationService configurationService,
        GraphMLImporterService graphMLImporter,
        NetworkEditorViewModel networkEditor,
        SimulationViewModel simulation)
    {
        _configurationService = configurationService;
        _graphMLImporter = graphMLImporter;
        _networkEditor = networkEditor;
        _simulation = simulation;

        NewCommand = ReactiveCommand.Create(New);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync);
        ExitCommand = ReactiveCommand.Create(Exit);
        ImportGraphMLCommand = ReactiveCommand.CreateFromTask(ImportGraphMLAsync);

        New();
    }

    private void New()
    {
        CurrentFilePath = null;
        CurrentDesign = new NetworkDesignModel();
        Title = "MqttY Designer - New Configuration";
    }

    private async Task OpenAsync()
    {
        var dialog = new Avalonia.Controls.OpenFileDialog
        {
            Title = "Open Network Configuration",
            Filters = new()
            {
                new() { Name = "JSON Files", Extensions = { "json" } },
                new() { Name = "All Files", Extensions = { "*" } }
            },
            AllowMultiple = false
        };

        var window = GetMainWindow();
        if (window == null) return;

        var result = await dialog.ShowAsync(window);
        if (result == null || result.Length == 0) return;

        try
        {
            CurrentDesign = await _configurationService.LoadFromFileAsync(result[0]);
            CurrentFilePath = result[0];
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(window, $"Failed to load configuration: {ex.Message}");
        }
    }

    private async Task SaveAsync()
    {
        if (CurrentDesign == null) return;

        if (!string.IsNullOrEmpty(CurrentFilePath))
        {
            try
            {
                await _configurationService.SaveToFileAsync(CurrentDesign, CurrentFilePath);
            }
            catch (Exception ex)
            {
                var window = GetMainWindow();
                if (window != null)
                    await ShowErrorAsync(window, $"Failed to save configuration: {ex.Message}");
            }
        }
        else
        {
            await SaveAsAsync();
        }
    }

    private async Task SaveAsAsync()
    {
        if (CurrentDesign == null) return;

        var dialog = new Avalonia.Controls.SaveFileDialog
        {
            Title = "Save Network Configuration",
            Filters = new()
            {
                new() { Name = "JSON Files", Extensions = { "json" } }
            },
            DefaultExtension = "json"
        };

        var window = GetMainWindow();
        if (window == null) return;

        var result = await dialog.ShowAsync(window);
        if (string.IsNullOrEmpty(result)) return;

        try
        {
            await _configurationService.SaveToFileAsync(CurrentDesign, result);
            CurrentFilePath = result;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(window, $"Failed to save configuration: {ex.Message}");
        }
    }

    private void Exit()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        lifetime?.Shutdown();
    }

    private async Task ImportGraphMLAsync()
    {
        var dialog = new Avalonia.Controls.OpenFileDialog
        {
            Title = "Import GraphML",
            Filters = new()
            {
                new() { Name = "GraphML Files", Extensions = { "graphml", "xml" } },
                new() { Name = "All Files", Extensions = { "*" } }
            },
            AllowMultiple = false
        };

        var window = GetMainWindow();
        if (window == null) return;

        var result = await dialog.ShowAsync(window);
        if (result == null || result.Length == 0) return;

        try
        {
            CurrentDesign = await _graphMLImporter.ImportFromFileAsync(result[0]);
            CurrentFilePath = null;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(window, $"Failed to import GraphML: {ex.Message}");
        }
    }

    private void UpdateTitle()
    {
        Title = string.IsNullOrEmpty(CurrentFilePath)
            ? "MqttY Designer - New Configuration"
            : $"MqttY Designer - {CurrentFilePath}";
    }

    private static Avalonia.Controls.Window? GetMainWindow()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        return lifetime?.MainWindow;
    }

    private static async Task ShowErrorAsync(Avalonia.Controls.Window owner, string message)
    {
        var dialog = new Avalonia.Controls.Window
        {
            Title = "Error",
            Content = new Avalonia.Controls.TextBlock
            {
                Text = message,
                Margin = new Avalonia.Thickness(20),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            Width = 400,
            Height = 200,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await dialog.ShowDialog(owner);
    }
}