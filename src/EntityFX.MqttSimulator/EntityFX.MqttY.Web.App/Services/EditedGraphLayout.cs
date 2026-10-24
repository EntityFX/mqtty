using EntityFX.MqttY.Federation;
using EntityFX.MqttY.Contracts.Options;
namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Преобразует физический граф в native-конфигурацию без восстановления шаблона.</summary>
internal sealed record EditedGraphLayout(FederationPhysicalLayout Physical, SiteLink[] Links, int[] PublisherSites)
{
    public static EditedGraphLayout Read(NetworkGraphOption graph)
    {
        var networks=graph.Networks.Keys.ToArray();
        if(networks.Length == 0)throw new ArgumentException("Добавьте хотя бы одну сеть.");
        var index=networks.Select((name,i)=>(name,i)).ToDictionary(x=>x.name,x=>x.i);
        var brokers=graph.Nodes.Where(n=>n.Value.Type==NodeOptionType.Server).ToArray();
        if(brokers.Length == 0)throw new ArgumentException("Добавьте брокер.");
        var placements=brokers.Select(b=>b.Value.Network != null && index.TryGetValue(b.Value.Network,out var i)?i:throw new ArgumentException("У брокера не задана существующая сеть: "+b.Key)).ToArray();
        if(placements.Distinct().Count()!=placements.Length)throw new ArgumentException("В текущей native-модели поддерживается один брокер на площадку. Разместите брокеры в разных сетях.");
        var sources=new List<int>();var sourceNames=new List<string>();
        foreach(var client in graph.Nodes.Where(n=>n.Value.Type==NodeOptionType.Client))
        {
            var target=Array.FindIndex(brokers,b=>b.Key==client.Value.ConnectsToServer);
            if(target<0)target=Array.FindIndex(brokers,b=>b.Value.Network==client.Value.Network);
            if(target<0||brokers[target].Value.Network!=client.Value.Network)throw new ArgumentException("У клиента должна быть сеть его брокера: "+client.Key);
            sources.Add(target);sourceNames.Add(client.Key);
        }
        if(sources.Count<brokers.Length||Enumerable.Range(0,brokers.Length).Any(b=>!sources.Contains(b)))throw new ArgumentException("На каждом брокере нужен хотя бы один отправитель.");
        var edges=graph.Networks.SelectMany(n=>n.Value.Links.Select(l=>{
            if(l.Network==null||!index.ContainsKey(l.Network))throw new ArgumentException("Связь ведёт в неизвестную сеть.");
            var a=index[n.Key];var b=index[l.Network];return new SiteLink(Math.Min(a,b),Math.Max(a,b));
        })).Distinct().ToArray();
        var reached=new HashSet<int>{0};
        for(var i=0;i<networks.Length;i++)foreach(var edge in edges){if(reached.Contains(edge.A))reached.Add(edge.B);if(reached.Contains(edge.B))reached.Add(edge.A);}
        if(reached.Count!=networks.Length)throw new ArgumentException("Граф состоит из отдельных частей. Соедините сети добавленных топологий перед запуском.");
        return new(new FederationPhysicalLayout(networks,placements,brokers.Select(b=>b.Key).ToArray(),sourceNames.ToArray()),edges,sources.ToArray());
    }
}
