using EntityFX.MqttY.Contracts.Options;
using ReactiveUI;

namespace EntityFX.MqttY.Designer.Models;

public class NodeDesignModel : ReactiveObject
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
}