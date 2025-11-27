using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;

namespace EntityFX.Tests.Integration.Helpers;

public class NetworkBulder
{
    private readonly INetworkSimulator networkSimulator;
    private int _nextId = 0;

    public NetworkBulder(INetworkSimulator networkSimulator)
    {
        this.networkSimulator = networkSimulator;
    }

    public Network BuildTree(int branchingFactor, int depth, TicksOptions ticksOptions)
    {
        if (branchingFactor < 1 || depth < 1)
            throw new ArgumentException("Параметры должны быть положительными числами");

        return CreateNetwork(branchingFactor, depth, "net", new NetworkTypeOption()
        {
            NetworkType = "eth",
            RefreshTicks = 2,
            SendTicks = 3,
            Speed = 18750000
        }, ticksOptions);
    }

    private Network CreateNetwork(int branchingFactor, int depth, string namePrefix, NetworkTypeOption networkTypeOption, TicksOptions ticksOptions)
    {
        var ix = _nextId++;
        var name = $"n{ix}.{namePrefix}";
        var node = new Network(ix, name, name, "eth", networkTypeOption, ticksOptions);
        networkSimulator.AddNetwork(node);
        if (depth > 1)
        {
            for (int i = 0; i < branchingFactor; i++)
            {
                var child = CreateNetwork(branchingFactor, depth - 1, name, networkTypeOption, ticksOptions);
                child.Link(node);

            }
        }

        return node;
    }
}
