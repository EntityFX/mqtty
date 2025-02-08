using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetwork : INode
    {
        IReadOnlyDictionary<string, INetwork> LinkedNearestNetworks { get; }
        IReadOnlyDictionary<string, IServer> Servers { get; }
        IReadOnlyDictionary<string, IClient> Clients { get; }

        bool Link(INetwork network);

        bool Unlink(INetwork network);

        bool UnlinkAll();

        bool AddServer(IServer server);

        bool RemoveServer(string server);

        bool AddClient(IClient client);

        bool RemoveClient(string clientAddress);

        INode? FindNode(string nodeAddress, NodeType type);

    }
}
