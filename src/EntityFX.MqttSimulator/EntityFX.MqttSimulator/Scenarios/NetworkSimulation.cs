﻿using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Scenarios
{
    public class NetworkSimulation
    {
        public INetworkSimulator? NetworkGraph { get; set; }
    }
}
