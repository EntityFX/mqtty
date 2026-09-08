namespace EntityFX.MqttY.Contracts.Options;

public class NetworkOptions
{
    /// <summary>Admission capacity in bytes per second, including packet headers.</summary>
    public double CapacityBytesPerSecond { get; set; }

    /// <summary>Legacy bytes-per-second alias. No implicit multiplier or bits conversion is applied.</summary>
    public int Speed
    {
        get => checked((int)CapacityBytesPerSecond);
        set => CapacityBytesPerSecond = value;
    }

    /// <summary>Rolling admission window; budget = capacity * tick period * window ticks.</summary>
    public int ThroughputWindowTicks { get; set; } = 100;

    public int QueueCapacity { get; set; } = 50000;

    public int TransferTicks { get; set; }

    public string NetworkType { get; set; } = string.Empty;

    public void Validate()
    {
        if (!double.IsFinite(CapacityBytesPerSecond) || CapacityBytesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(CapacityBytesPerSecond));
        if (QueueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
        if (ThroughputWindowTicks <= 0) throw new ArgumentOutOfRangeException(nameof(ThroughputWindowTicks));
        if (TransferTicks <= 0) throw new ArgumentOutOfRangeException(nameof(TransferTicks));
    }
}
