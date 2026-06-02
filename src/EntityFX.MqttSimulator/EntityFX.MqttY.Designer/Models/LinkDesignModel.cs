using ReactiveUI;

namespace EntityFX.MqttY.Designer.Models;

public class LinkDesignModel : ReactiveObject
{
    private string? _targetNetwork;
    private int? _weight;

    public string? TargetNetwork
    {
        get => _targetNetwork;
        set => this.RaiseAndSetIfChanged(ref _targetNetwork, value);
    }

    public int? Weight
    {
        get => _weight;
        set => this.RaiseAndSetIfChanged(ref _weight, value);
    }
}