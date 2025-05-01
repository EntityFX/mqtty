namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class PacketIdProvider
    {
        readonly object _lockObject;
        volatile ushort _lastValue;

        public PacketIdProvider()
        {
            _lockObject = new object();
            _lastValue = 0;
        }

        public ushort GetPacketId()
        {
            var id = default(ushort);

            lock (_lockObject)
            {
                if (_lastValue == ushort.MaxValue)
                {
                    id = 1;
                }
                else
                {
                    id = (ushort)(_lastValue + 1);
                }

                _lastValue = id;
            }

            return id;
        }
    }
}
