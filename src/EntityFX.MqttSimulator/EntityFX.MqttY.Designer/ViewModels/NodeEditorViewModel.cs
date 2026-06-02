using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Designer.Models;
using ReactiveUI;

namespace EntityFX.MqttY.Designer.ViewModels;

public class NodeEditorViewModel : ReactiveObject
{
    private string _name = string.Empty;
    private NodeOptionType _type;
    private string? _protocol;
    private string? _specification;
    private string? _network;
    private string? _connectsToServer;
    private int? _quantity;
    private int? _index;
    private object? _configuration;
    private string? _errorMessage;

    private readonly List<string> _availableNetworks;
    private readonly List<string> _availableProtocols = new() { "mqtt", "http", "custom" };
    private readonly List<string> _availableSpecifications = new()
    {
        "mqtt-client", "mqtt-server", "mqtt-relay", "mqtt-receiver",
        "http-client", "http-server", "custom"
    };

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public NodeOptionType Type
    {
        get => _type;
        set => this.RaiseAndSetIfChanged(ref _type, value);
    }

    public string? Protocol
    {
        get => _protocol;
        set => this.RaiseAndSetIfChanged(ref _protocol, value);
    }

    public string? Specification
    {
        get => _specification;
        set => this.RaiseAndSetIfChanged(ref _specification, value);
    }

    public string? Network
    {
        get => _network;
        set => this.RaiseAndSetIfChanged(ref _network, value);
    }

    public string? ConnectsToServer
    {
        get => _connectsToServer;
        set => this.RaiseAndSetIfChanged(ref _connectsToServer, value);
    }

    public int? Quantity
    {
        get => _quantity;
        set => this.RaiseAndSetIfChanged(ref _quantity, value);
    }

    public int? Index
    {
        get => _index;
        set => this.RaiseAndSetIfChanged(ref _index, value);
    }

    public object? Configuration
    {
        get => _configuration;
        set => this.RaiseAndSetIfChanged(ref _configuration, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public List<string> AvailableNetworks => _availableNetworks;
    public List<string> AvailableProtocols => _availableProtocols;
    public List<string> AvailableSpecifications => _availableSpecifications;

    public List<NodeOptionType> AvailableTypes { get; } = new()
    {
        NodeOptionType.Client,
        NodeOptionType.Server,
        NodeOptionType.Application
    };

    public NodeEditorViewModel(NodeDesignModel node, List<string> availableNetworks)
    {
        _availableNetworks = availableNetworks;

        // Copy values from the model
        _name = node.Name;
        _type = node.Type;
        _protocol = node.Protocol;
        _specification = node.Specification;
        _network = node.Network;
        _connectsToServer = node.ConnectsToServer;
        _quantity = node.Quantity;
        _index = node.Index;
        _configuration = node.Configuration;
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Network))
        {
            ErrorMessage = "Network must be selected.";
            return false;
        }

        ErrorMessage = null;
        return true;
    }

    public void ApplyTo(NodeDesignModel node)
    {
        node.Name = Name;
        node.Type = Type;
        node.Protocol = Protocol;
        node.Specification = Specification;
        node.Network = Network;
        node.ConnectsToServer = ConnectsToServer;
        node.Quantity = Quantity;
        node.Index = Index;
        node.Configuration = Configuration;
    }
}