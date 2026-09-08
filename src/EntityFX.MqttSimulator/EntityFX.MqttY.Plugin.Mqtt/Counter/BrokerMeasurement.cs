using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Counter
{
    internal sealed class BrokerMeasurement
    {
        private sealed class Accumulator
        {
            public long Attempted;
            public long Admitted;
            public long Completed;
            public long ExpectedDeliveries;
            public long Delivered;
            public long RateRejected;
            public long PublishFailed;
            public long DeliveryDropped;
            public LatencyHistogram Latency { get; } = new();
        }

        internal sealed class PublishTracker
        {
            public string Publisher { get; init; } = string.Empty;
            public MqttQos Qos { get; init; }
            public ushort? PacketId { get; init; }
            public long NetworkPacketId { get; init; }
            public long Generation { get; init; }
            public long StartTick { get; init; }
            public bool Admitted { get; set; }
        }

        private readonly object _sync = new();
        private readonly Dictionary<string, PublishTracker> _publishes = new();
        private readonly Dictionary<long, (long Generation, MqttQos Qos)> _deliveries = new();
        private Accumulator[] _accumulators = CreateAccumulators();
        private long _generation;
        private long _startTick;

        public PublishTracker BeginAttempt(
            string publisher, MqttQos qos, ushort? packetId, long networkPacketId, long startTick)
        {
            lock (_sync)
            {
                var key = Key(publisher, qos, packetId, networkPacketId);
                if (_publishes.TryGetValue(key, out var existing)) return existing;

                var tracker = new PublishTracker
                {
                    Publisher = publisher,
                    Qos = qos,
                    PacketId = packetId,
                    NetworkPacketId = networkPacketId,
                    Generation = _generation,
                    StartTick = startTick
                };
                _publishes[key] = tracker;
                _accumulators[(int)qos].Attempted++;
                return tracker;
            }
        }

        public void Admit(PublishTracker tracker)
        {
            lock (_sync)
            {
                if (tracker.Admitted) return;
                tracker.Admitted = true;
                if (tracker.Generation != _generation) return;
                var accumulator = _accumulators[(int)tracker.Qos];
                accumulator.Admitted++;
                if (tracker.Qos == MqttQos.AtMostOnce) accumulator.Completed++;
            }
        }

        public void RejectByRate(PublishTracker tracker) => Reject(tracker, rate: true);

        public void FailPublish(PublishTracker tracker) => Reject(tracker, rate: false);

        private void Reject(PublishTracker tracker, bool rate)
        {
            lock (_sync)
            {
                if (tracker.Generation == _generation)
                {
                    if (rate) _accumulators[(int)tracker.Qos].RateRejected++;
                    else _accumulators[(int)tracker.Qos].PublishFailed++;
                }
                _publishes.Remove(Key(tracker.Publisher, tracker.Qos, tracker.PacketId, tracker.NetworkPacketId));
            }
        }

        public PublishTracker? Find(
            string publisher, MqttQos qos, ushort? packetId, long networkPacketId)
        {
            lock (_sync)
            {
                _publishes.TryGetValue(Key(publisher, qos, packetId, networkPacketId), out var tracker);
                return tracker;
            }
        }

        public void ExpectDelivery(PublishTracker? tracker, long outgoingPacketId)
        {
            if (tracker == null) return;
            lock (_sync)
            {
                _deliveries[outgoingPacketId] = (tracker.Generation, tracker.Qos);
                if (tracker.Generation == _generation && tracker.Admitted)
                    _accumulators[(int)tracker.Qos].ExpectedDeliveries++;
            }
        }

        public void DeliveryCompleted(long outgoingPacketId)
        {
            lock (_sync)
            {
                if (!_deliveries.Remove(outgoingPacketId, out var delivery)) return;
                if (delivery.Generation == _generation) _accumulators[(int)delivery.Qos].Delivered++;
            }
        }

        public void DeliveryDropped(long outgoingPacketId)
        {
            lock (_sync)
            {
                if (!_deliveries.Remove(outgoingPacketId, out var delivery)) return;
                if (delivery.Generation == _generation) _accumulators[(int)delivery.Qos].DeliveryDropped++;
            }
        }

        public long? CompletePublisher(string publisher, MqttQos qos, ushort packetId, long endTick)
        {
            lock (_sync)
            {
                var key = Key(publisher, qos, packetId, 0);
                if (!_publishes.Remove(key, out var tracker) || !tracker.Admitted ||
                    tracker.Generation != _generation)
                    return null;

                var elapsedTicks = Math.Max(0, endTick - tracker.StartTick);
                var accumulator = _accumulators[(int)qos];
                accumulator.Completed++;
                accumulator.Latency.Record(elapsedTicks);
                return elapsedTicks;
            }
        }

        public void CompleteFanOut(PublishTracker? tracker)
        {
            if (tracker?.Qos != MqttQos.AtMostOnce) return;
            lock (_sync)
                _publishes.Remove(Key(tracker.Publisher, tracker.Qos, tracker.PacketId, tracker.NetworkPacketId));
        }

        public void Reset(long startTick)
        {
            lock (_sync)
            {
                _generation++;
                _startTick = startTick;
                _accumulators = CreateAccumulators();
            }
        }

        public BrokerMetricsSnapshot Snapshot(long endTick, TimeSpan tickPeriod)
        {
            lock (_sync)
            {
                var seconds = Math.Max(0, endTick - _startTick) * tickPeriod.TotalSeconds;
                var values = Enum.GetValues<MqttQos>().ToDictionary(qos => qos, qos =>
                {
                    var accumulator = _accumulators[(int)qos];
                    double? ToMs(long? ticks) => ticks * tickPeriod.TotalMilliseconds;
                    return new BrokerQosMetricsSnapshot(
                        accumulator.Attempted,
                        accumulator.Admitted,
                        accumulator.Completed,
                        accumulator.ExpectedDeliveries,
                        accumulator.Delivered,
                        accumulator.RateRejected,
                        accumulator.PublishFailed,
                        accumulator.DeliveryDropped,
                        seconds > 0 ? accumulator.Completed / seconds : 0,
                        qos == MqttQos.AtMostOnce ? null : ToMs(accumulator.Latency.NearestRank(0.50)),
                        qos == MqttQos.AtMostOnce ? null : ToMs(accumulator.Latency.NearestRank(0.95)),
                        qos == MqttQos.AtMostOnce ? null : ToMs(accumulator.Latency.NearestRank(0.99)));
                });

                return new BrokerMetricsSnapshot(_startTick, endTick, BrokerMetricsSnapshot.ReadOnly(values));
            }
        }

        private static Accumulator[] CreateAccumulators() =>
            Enum.GetValues<MqttQos>().Select(_ => new Accumulator()).ToArray();

        private static string Key(string publisher, MqttQos qos, ushort? packetId, long networkPacketId) =>
            qos == MqttQos.AtMostOnce
                ? $"{publisher}|{(int)qos}|n{networkPacketId}"
                : $"{publisher}|{(int)qos}|p{packetId}";
    }
}
