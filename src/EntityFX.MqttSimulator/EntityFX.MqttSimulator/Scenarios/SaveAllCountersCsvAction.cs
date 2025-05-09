using System.Text;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Helper;

namespace EntityFX.MqttY.Scenarios;

public class SaveAllCountersCsvAction : ScenarioAction<NetworkSimulation, PathOptions>
{
    public override Task ExecuteAsync()
    {
        if (Config == null)
        {
            throw new ArgumentNullException(nameof(Config));
        }

        var path = FileExtensions.ReplaceParams(Config.Path, Scenario!.Name!);
        FileExtensions.CreateDirectory(path);

        foreach (var network in Context!.NetworkGraph!.Networks)
        {
            var networkPath = Path.Combine(path, network.Key);
            FileExtensions.CreateDirectory(networkPath);

            foreach (var node in network.Value.Nodes)
            {
                var nodePath = Path.Combine(networkPath, node.Key);
                FileExtensions.CreateDirectory(nodePath);

                VisitCounter(node.Value.Counters, nodePath);
            }
        }
        
        return Task.CompletedTask;
    }
    
    private void VisitCounter(ICounter counter, string path)
    {
        if (counter is CounterGroup counterGroup)
        {
            foreach (var counterItem in counterGroup.Counters.ToArray())
            {
                VisitCounter(counterItem, path);
            }
            
        }
        else
        {
            if (counter.HistoryValues.Any() != true)
            {
                return;
            }
            
            var ch = CounterHistoryToCsv(counter);

            var csvPath = Path.Combine(path, $"{counter.Name}.csv");
            
            File.WriteAllText(csvPath, ch);
        }
    }

    private string CounterHistoryToCsv(ICounter counter)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Tick;Value");

        foreach (var historyValue in counter.HistoryValues)
        {
            sb.AppendLine($"{historyValue.Key};{historyValue.Value}");
        }

        return sb.ToString();
    }
}