namespace EntityFX.MqttY.Federation;

public interface IForwardingPolicy
{
    int[] Destinations(int site, int origin, int sender);
}
