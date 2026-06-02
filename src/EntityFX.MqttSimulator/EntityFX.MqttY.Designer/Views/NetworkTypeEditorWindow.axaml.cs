using Avalonia.Controls;
using EntityFX.MqttY.Designer.ViewModels;

namespace EntityFX.MqttY.Designer.Views;

public partial class NetworkTypeEditorWindow : Window
{
    public NetworkTypeEditorWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is NetworkTypeEditorViewModel vm)
        {
            if (vm.Validate())
            {
                Close(true);
            }
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}