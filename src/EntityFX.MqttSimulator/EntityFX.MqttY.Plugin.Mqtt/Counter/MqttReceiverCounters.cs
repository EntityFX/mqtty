﻿using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Counter;

namespace EntityFX.MqttY.Plugin.Mqtt.Counter
{
    internal class MqttReceiverCounters : CounterGroup
    {
        private readonly List<ICounter> _counters = new List<ICounter>();
        private GenericCounter _receiveCounter;

        public MqttReceiverCounters(string name)
            : base(name) 
        {
            _receiveCounter = new GenericCounter("Received");
            _counters.Add(_receiveCounter);
            Counters = _counters.ToArray();
        }

        public void Receive()
        {
            _receiveCounter.Increment();
        }
    }
}
