using EntityFX.MqttY.Designer.Models;
using ReactiveUI;

namespace EntityFX.MqttY.Designer.ViewModels;

public class NetworkTypeEditorViewModel : ReactiveObject
{
    private string _name = string.Empty;
    private int _speed;
    private int _refreshTicks;
    private int _sendTicks;
    private long _queueSize;
    private string? _errorMessage;

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public int Speed
    {
        get => _speed;
        set => this.RaiseAndSetIfChanged(ref _speed, value);
    }

    public int RefreshTicks
    {
        get => _refreshTicks;
        set => this.RaiseAndSetIfChanged(ref _refreshTicks, value);
    }

    public int SendTicks
    {
        get => _sendTicks;
        set => this.RaiseAndSetIfChanged(ref _sendTicks, value);
    }

    public long QueueSize
    {
        get => _queueSize;
        set => this.RaiseAndSetIfChanged(ref _queueSize, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public NetworkTypeEditorViewModel(NetworkTypeModel networkType)
    {
        _name = networkType.Name;
        _speed = networkType.Speed;
        _refreshTicks = networkType.RefreshTicks;
        _sendTicks = networkType.SendTicks;
        _queueSize = networkType.QueueSize;
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

    public void ApplyTo(NetworkTypeModel networkType)
    {
        networkType.Name = Name;
        networkType.Speed = Speed;
        networkType.RefreshTicks = RefreshTicks;
        networkType.SendTicks = SendTicks;
        networkType.QueueSize = QueueSize;
    }
}