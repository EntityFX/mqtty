using System.Collections.ObjectModel;
using EntityFX.MqttY.Designer.Models;
using ReactiveUI;

namespace EntityFX.MqttY.Designer.ViewModels;

public class EditNetworkViewModel : ReactiveObject
{
    private string _name = string.Empty;
    private int _index;
    private string _networkType = string.Empty;
    private string? _errorMessage;

    private readonly List<string> _availableNetworkTypes;
    private readonly ObservableCollection<LinkDesignModel> _links = new();

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

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public List<string> AvailableNetworkTypes => _availableNetworkTypes;
    public ObservableCollection<LinkDesignModel> Links => _links;

    public EditNetworkViewModel(NetworkNodeDesignModel network, List<string> availableNetworkTypes)
    {
        _availableNetworkTypes = availableNetworkTypes;

        _name = network.Name;
        _index = network.Index;
        _networkType = network.NetworkType;

        foreach (var link in network.Links)
        {
            _links.Add(new LinkDesignModel
            {
                TargetNetwork = link.TargetNetwork,
                Weight = link.Weight
            });
        }
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Name is required.";
            return false;
        }

        ErrorMessage = null;
        return true;
    }

    public void ApplyTo(NetworkNodeDesignModel network)
    {
        network.Name = Name;
        network.Index = Index;
        network.NetworkType = NetworkType;

        network.Links.Clear();
        foreach (var link in _links)
        {
            network.Links.Add(new LinkDesignModel
            {
                TargetNetwork = link.TargetNetwork,
                Weight = link.Weight
            });
        }
    }
}