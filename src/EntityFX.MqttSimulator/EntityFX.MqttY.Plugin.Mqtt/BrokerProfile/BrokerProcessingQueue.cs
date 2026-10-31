using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile
{
    /// <summary>
    /// Очередь обработки пакетов брокера, моделирующая задержку обработки (p99)
    /// через декремент остаточного времени в тиках.
    /// </summary>
    internal sealed class BrokerProcessingQueue
    {
        private readonly Queue<PendingItem> _items = new();
    private readonly List<INetworkPacket> _readyBuffer = new(16);

        public int Count => _items.Count;

        public bool IsEmpty => _items.Count == 0;

        public void Enqueue(INetworkPacket packet, int processingTicks)
        {
            if (processingTicks < 0) throw new ArgumentOutOfRangeException(nameof(processingTicks));
            _items.Enqueue(new PendingItem(packet, processingTicks));
        }

        /// <summary>
        /// Уменьшает остаточное время всех пакетов на один тик и возвращает
        /// пакеты, готовые к обработке.
        /// </summary>
        public List<INetworkPacket> DrainReady()
        {
            _readyBuffer.Clear();
            var count = _items.Count;

            for (var i = 0; i < count; i++)
            {
                var item = _items.Dequeue();
                item.RemainingTicks--;

                if (item.RemainingTicks <= 0)
                {
                    _readyBuffer.Add(item.Packet);
                }
                else
                {
                    _items.Enqueue(item);
                }
            }

            return _readyBuffer;
        }

        public void Clear()
        {
            _items.Clear();
        }

        private struct PendingItem
        {
            public PendingItem(INetworkPacket packet, int remainingTicks)
            {
                Packet = packet;
                RemainingTicks = remainingTicks;
            }

            public INetworkPacket Packet { get; }

            public int RemainingTicks { get; set; }
        }
    }
}
