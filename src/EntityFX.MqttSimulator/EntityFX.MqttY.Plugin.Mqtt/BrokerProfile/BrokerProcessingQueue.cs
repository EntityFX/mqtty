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

        public int Count => _items.Count;

        public bool IsEmpty => _items.Count == 0;

        public void Enqueue(INetworkPacket packet, int processingTicks)
        {
            _items.Enqueue(new PendingItem(packet, Math.Max(1, processingTicks)));
        }

        /// <summary>
        /// Уменьшает остаточное время всех пакетов на один тик и возвращает
        /// пакеты, готовые к обработке.
        /// </summary>
        public List<INetworkPacket> DrainReady()
        {
            var ready = new List<INetworkPacket>();
            var count = _items.Count;

            for (var i = 0; i < count; i++)
            {
                var item = _items.Dequeue();
                item.RemainingTicks--;

                if (item.RemainingTicks <= 0)
                {
                    ready.Add(item.Packet);
                }
                else
                {
                    _items.Enqueue(item);
                }
            }

            return ready;
        }

        public void Clear()
        {
            _items.Clear();
        }

        private sealed class PendingItem
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
