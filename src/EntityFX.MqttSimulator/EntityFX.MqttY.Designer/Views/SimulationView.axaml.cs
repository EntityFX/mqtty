using Avalonia.Controls;
using Avalonia.ReactiveUI;
using EntityFX.MqttY.Designer.ViewModels;
using ReactiveUI;

namespace EntityFX.MqttY.Designer.Views;

public partial class SimulationView : ReactiveUserControl<SimulationViewModel>
{
    public SimulationView()
    {
        InitializeComponent();
    }
}