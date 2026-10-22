namespace EntityFX.MqttY.Federation;

public static class FederationTopics
{
    public const string InputFilter = "federation/input/+";
    public const string RoutingFilter = "federation/routing/#";
    public const string DeliveredFilter = "federation/delivered/+";
    public static string Input(int origin) => $"federation/input/{origin}";
    public static string Delivered(int origin) => $"federation/delivered/{origin}";
    public static string Relay(int origin) => $"federation/routing/{origin}";
    public static string Routing(int origin, long sequence, int sender, int remaining) => $"federation/routing/{origin}/{sequence}/{sender}/{remaining}";
    public static (int Origin, long Sequence, int Sender, int Remaining) Parse(string topic)
    {
        var p = topic.Split('/');
        if (p.Length != 6 || p[0] != "federation" || p[1] != "routing" ||
            !int.TryParse(p[2], out var origin) || origin < 0 || !long.TryParse(p[3], out var sequence) || sequence < 1 ||
            !int.TryParse(p[4], out var sender) || sender < 0 || !int.TryParse(p[5], out var remaining) || remaining < 0)
            throw new InvalidDataException("Invalid federation envelope.");
        return (origin, sequence, sender, remaining);
    }
}
