using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Contracts.Options
{
    public class NetworkGraphOptions
    {
        public Dictionary<string, IEnumerable<NetworkOption?>?> Networks { get; set; } = new();

        public Dictionary<string, NodeOption> Nodes { get; set; } = new();
    }
}
