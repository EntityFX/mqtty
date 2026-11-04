namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class PacketIdProvider
    {
        // int (not ushort) so it can be used with Interlocked CAS; valid ids are 1..ushort.MaxValue.
        private int _lastValue;

        public PacketIdProvider()
        {
            _lastValue = 0;
        }

        public ushort GetPacketId()
        {
            while (true)
            {
                var last = Volatile.Read(ref _lastValue);
                var next = last >= ushort.MaxValue ? 1 : last + 1;
                if (Interlocked.CompareExchange(ref _lastValue, next, last) == last)
                {
                    return (ushort)next;
                }
            }
        }
    }
}
