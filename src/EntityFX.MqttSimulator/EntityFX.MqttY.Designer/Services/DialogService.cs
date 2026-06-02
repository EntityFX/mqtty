using Avalonia.Controls;
using EntityFX.MqttY.Designer.Models;
using EntityFX.MqttY.Designer.ViewModels;
using EntityFX.MqttY.Designer.Views;

namespace EntityFX.MqttY.Designer.Services;

public class DialogService : IDialogService
{
    private static Window GetMainWindow()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        return lifetime?.MainWindow ?? throw new InvalidOperationException("Main window is not available.");
    }

    public async Task<bool> ShowNodeEditorAsync(NodeDesignModel node, List<string> availableNetworks)
    {
        var viewModel = new NodeEditorViewModel(node, availableNetworks);
        var dialog = new NodeEditorWindow
        {
            DataContext = viewModel
        };
        var result = await dialog.ShowDialog<bool>(GetMainWindow());
        if (result)
        {
            viewModel.ApplyTo(node);
        }
        return result;
    }

    public async Task<bool> ShowNetworkTypeEditorAsync(NetworkTypeModel networkType)
    {
        var viewModel = new NetworkTypeEditorViewModel(networkType);
        var dialog = new NetworkTypeEditorWindow
        {
            DataContext = viewModel
        };
        var result = await dialog.ShowDialog<bool>(GetMainWindow());
        if (result)
        {
            viewModel.ApplyTo(networkType);
        }
        return result;
    }

    public async Task<bool> ShowNetworkEditorAsync(NetworkNodeDesignModel network, List<string> availableNetworkTypes)
    {
        var viewModel = new EditNetworkViewModel(network, availableNetworkTypes);
        var dialog = new EditNetworkWindow
        {
            DataContext = viewModel
        };
        var result = await dialog.ShowDialog<bool>(GetMainWindow());
        if (result)
        {
            viewModel.ApplyTo(network);
        }
        return result;
    }
}