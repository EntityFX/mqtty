using System.Collections.ObjectModel;
using ReactiveUI;

namespace EntityFX.MqttY.Designer.Models;

public class NetworkNodeDesignModel : ReactiveObject
{
    private string _name = string.Empty;
    private int _index;
    private string _networkType = string.Empty;

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public int Index
    {
        get => _index;
        set => this.RaiseAndSetIfChanged(ref _index, value);
    }

    public string NetworkType
    {
        get => _networkType;
        set => this.RaiseAndSetIfChanged(ref _networkType, value);
    }

    public ObservableCollection<LinkDesignModel> Links { get; set; } = new();
}