using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Contracts.Options
{
    public class NetworkGraphOption
    {
        public SortedDictionary<string, NetworkNodeOption> Networks { get; set; } = new();

        public SortedDictionary<string, NodeOption> Nodes { get; set; } = new();

    }
}
