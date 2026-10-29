using System.Text.Json;

namespace EntityFX.MqttY.Engines.TwinValidation;

/// <summary>Use successful publication IDs as the loss denominator, including outcomes during drain.</summary>
internal static class NativeControlCohort
{
    public static JsonElement Read(string directory)
    {
        using var phases=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory,"phases.json")));
        var p=phases.RootElement;var begin=p.GetProperty("measurementStartSeconds").GetDouble();var end=p.GetProperty("measurementEndSeconds").GetDouble();var horizon=p.GetProperty("drainEndSeconds").GetDouble();
        if(!(begin<end && end<=horizon) || !p.GetProperty("completed").GetBoolean())throw new InvalidDataException("Незавершённые фазы сравнения.");
        var attempts=new Dictionary<string,double>();var outcomes=new Dictionary<string,(double Time,bool Success,double Latency)>();
        foreach(var line in File.ReadLines(Path.Combine(directory,"publishes.ndjson")))
        {
            if(string.IsNullOrWhiteSpace(line))continue;using var doc=JsonDocument.Parse(line);var e=doc.RootElement;var id=e.GetProperty("id").GetString()!;var time=e.GetProperty("elapsedSeconds").GetDouble();var kind=e.GetProperty("event").GetString();
            if(kind=="attempt")attempts.Add(id,time);
            else if(kind is "completed" or "failed")outcomes.Add(id,(time,kind=="completed",kind=="completed"?e.GetProperty("latencyMs").GetDouble():0));
            else throw new InvalidDataException("Неизвестный исход публикации.");
        }
        var selected=attempts.Where(a=>a.Value>=begin && a.Value<end).Select(a=>a.Key).ToHashSet();
        var completed=selected.Where(id=>outcomes.TryGetValue(id,out var o)&&o.Time<=horizon&&o.Success).ToHashSet();
        var failed=selected.Count(id=>outcomes.TryGetValue(id,out var o)&&o.Time<=horizon&&!o.Success);var delivered=new HashSet<string>();
        foreach(var line in File.ReadLines(Path.Combine(directory,"deliveries.ndjson")))
        {
            if(string.IsNullOrWhiteSpace(line))continue;using var doc=JsonDocument.Parse(line);var e=doc.RootElement;var id=e.GetProperty("id").GetString()!;var time=e.GetProperty("elapsedSeconds").GetDouble();
            if(completed.Contains(id)&&time>=begin&&time<=horizon)delivered.Add(id);
        }
        var latency=completed.Select(id=>outcomes[id].Latency).OrderBy(x=>x).ToArray();var pending=selected.Count-completed.Count-failed;
        return JsonSerializer.SerializeToElement(new {attempted=selected.Count,completed=completed.Count,failed,pending,completedIdsDelivered=delivered.Count,outcomesSettled=pending==0,
            metrics=new Dictionary<string,double?> { ["rps"]=completed.Count/(end-begin),["p99"]=latency.Length==0?null:latency[Math.Max(0,(int)Math.Ceiling(.99*latency.Length)-1)],
                ["failure"]=selected.Count==0?null:(double)failed/selected.Count,["loss"]=completed.Count==0?null:1-(double)delivered.Count/completed.Count }});
    }
}
