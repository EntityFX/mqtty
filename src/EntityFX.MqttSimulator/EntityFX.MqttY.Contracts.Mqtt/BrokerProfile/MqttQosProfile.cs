namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    /// <summary>
    /// Эталонный профиль брокера для одного уровня качества обслуживания.
    /// Представляет собой набор опорных точек, по которым выполняется
    /// линейная интерполяция от числа подключённых клиентов.
    /// </summary>
    public sealed class MqttQosProfile
    {
        public IReadOnlyList<MqttQosSample> Samples { get; init; }
            = Array.Empty<MqttQosSample>();

        /// <summary>
        /// Возвращает эталонную точку (RPS, задержка, доля отказов) для заданного
        /// числа клиентов. За пределами диапазона закрепляются крайние значения.
        /// </summary>
        public MqttQosSample Interpolate(int clients)
        {
            var sorted = Samples;

            if (sorted.Count == 0)
            {
                return default;
            }

            var first = sorted[0];
            if (clients <= first.Clients)
            {
                return first;
            }

            var last = sorted[^1];
            if (clients >= last.Clients)
            {
                return last;
            }

            for (var i = 0; i < sorted.Count - 1; i++)
            {
                var a = sorted[i];
                var b = sorted[i + 1];

                if (clients < a.Clients || clients > b.Clients)
                {
                    continue;
                }

                var span = b.Clients - a.Clients;
                var t = span == 0 ? 0.0 : (clients - a.Clients) / (double)span;

                return new MqttQosSample(
                    clients,
                    a.Rps + t * (b.Rps - a.Rps),
                    a.LatencyMs + t * (b.LatencyMs - a.LatencyMs),
                    a.FailRate + t * (b.FailRate - a.FailRate));
            }

            return last;
        }
    }
}