using ReactiveUI;

namespace EntityFX.MqttY.Designer.Models;

public class NetworkTypeModel : ReactiveObject
{
    private string _name = string.Empty;
    private int _speed;
    private int _refreshTicks;
    private int _sendTicks;
    private long _queueSize;

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
}