using System.Globalization;
namespace EntityFX.MqttY.Federation;

public static class FederationConsoleView
{
    public static void Write(TextWriter writer, FederationOptions o, FederationSummary s)
    {
        string N(double? x) => x?.ToString("0.######", CultureInfo.InvariantCulture) ?? "notApplicable";
        writer.WriteLine($"Статическая федерация: {o.Mode}; площадок {o.Brokers}; QoS {o.Qos}; статус {s.Status}");
        writer.WriteLine("Физические связи: " + string.Join(", ", FederationGraph.Edges(o).Select(e => $"{e.A}<->{e.B}")));
        var max = s.Receivers.Select(r => r.UniqueDeliveries).DefaultIfEmpty(0).Max();
        foreach (var r in s.Receivers) writer.WriteLine($"subscriber@{r.Site}: [{new string('#', max == 0 ? 0 : (int)(r.UniqueDeliveries * 24 / max))}] {r.UniqueDeliveries}; дубликатов {r.Duplicates}");
        writer.WriteLine($"Publish попытки/завершения: {s.ApiAttempts}/{s.SourceCompleted}; source RPS={N(s.SourceCompletedRps)}; delivery goodput={N(s.UniqueGoodput)}; loss={N(s.DeliveryLoss)}");
        writer.WriteLine($"p99 ACK={N(s.PublishP99Ms)} мс; p99 получения={N(s.ReceiverP99Ms)} мс; пересылок={s.ForwardAttempts}; подавлено повторов={s.SuppressedDuplicates}; queue drops={s.QueueDrops}");
        if (s.Error != null) writer.WriteLine(s.Error);
    }
}
