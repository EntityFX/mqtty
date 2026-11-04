using System.Globalization;
using System.Security;
using System.Text;
namespace EntityFX.MqttY.Federation;

/// <summary>Диаграммы физического графа редактора и фактических логических соседей.</summary>
internal static class FederationCustomDiagrams
{
    public static void Write(string output,FederationOptions o)
    {
        foreach(var logical in new[]{false,true})
        {
            var names=logical?Enumerable.Range(0,o.Brokers).Select(i=>o.PhysicalLayout!.BrokerNames?[i]??$"broker{i:D4}").ToArray():o.PhysicalLayout!.NetworkNames;
            var edges=!logical?FederationGraph.Edges(o):o.Mode==FederationMode.Relay?
                Enumerable.Range(0,o.Brokers).SelectMany(a=>Enumerable.Range(a+1,o.Brokers-a-1).Select(b=>new SiteLink(a,b))).ToArray():FederationGraph.ForwardingEdges(o);
            var mmd=new StringBuilder("flowchart LR\n");
            var svg=new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1000 850\"><rect width=\"1000\" height=\"850\" fill=\"white\"/>");
            var positions=names.Select((_,i)=>(X:500+340*Math.Cos(2*Math.PI*i/names.Length),Y:420+310*Math.Sin(2*Math.PI*i/names.Length))).ToArray();
            static string N(double x)=>x.ToString("0.###",CultureInfo.InvariantCulture);
            foreach(var edge in edges)
            {
                var a=positions[edge.A];var b=positions[edge.B];
                svg.Append($"<line x1=\"{N(a.X)}\" y1=\"{N(a.Y)}\" x2=\"{N(b.X)}\" y2=\"{N(b.Y)}\" stroke=\"#71839b\" stroke-width=\"2\"/>");
                mmd.AppendLine($"  N{edge.A} <--> N{edge.B}");
            }
            for(var i=0;i<names.Length;i++)
            {
                var p=positions[i];var label=SecurityElement.Escape(names[i]);
                mmd.AppendLine($"  N{i}[\"{names[i].Replace("\"","'").Replace("\n"," ")}\"]");
                svg.Append($"<rect x=\"{N(p.X-95)}\" y=\"{N(p.Y-25)}\" width=\"190\" height=\"50\" rx=\"8\" fill=\"#eef4fb\" stroke=\"#435a72\"/><text x=\"{N(p.X)}\" y=\"{N(p.Y+5)}\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"13\">{label}</text>");
            }
            svg.Append("</svg>");var kind=logical?"logical":"physical";
            File.WriteAllText(Path.Combine(output,$"topology-{kind}.mmd"),mmd.ToString());
            File.WriteAllText(Path.Combine(output,$"topology-{kind}.svg"),svg.ToString());
        }
    }
}
