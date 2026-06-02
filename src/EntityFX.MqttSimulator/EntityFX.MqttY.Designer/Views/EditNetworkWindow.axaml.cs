using Avalonia.Controls;
using EntityFX.MqttY.Designer.ViewModels;

namespace EntityFX.MqttY.Designer.Views;

public partial class EditNetworkWindow : Window
{
    public EditNetworkWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is EditNetworkViewModel vm)
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