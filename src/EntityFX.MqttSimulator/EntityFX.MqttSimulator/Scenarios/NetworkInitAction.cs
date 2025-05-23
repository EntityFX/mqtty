﻿using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Helper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFX.MqttY.Scenarios
{
    public class NetworkInitAction : ScenarioAction<NetworkSimulation, NetworkGraphFactoryOption>
    {
        private ILogger<Scenario<NetworkSimulation>>? Logger { get; set; }
        
        public override Task ExecuteAsync()
        {
            if (Config == null)
            {
                throw new ArgumentNullException(nameof(Config));
            }

            Logger = ServiceProvider.GetRequiredService<ILogger<Scenario<NetworkSimulation>>>();

            if (Config.MonitoringOption.Path != null)
            {
                Config.MonitoringOption.Path = Config.MonitoringOption.Path
                    .Replace("{scenario}", Scenario.Name)
                    .Replace("{date}", $"{DateTime.Now:yyyy_MM_dd__HH_mm}");
            }

            Context!.NetworkGraph = Config.NetworkGraphFactory!.Create(Config);
            Context!.NetworkGraph.OnRefresh += NetworkGraph_OnRefresh;
            Context!.NetworkGraph!.StartPeriodicRefreshAsync();

            Config.NetworkGraphOption.Ticks = Config.TicksOption;
            Config.NetworkSimulatorBuilder!.OptionsPath = Config.OptionsPath;
            Config.NetworkSimulatorBuilder!.Configure(Context!.NetworkGraph, Config.NetworkGraphOption);

            return Task.CompletedTask;
        }

        private void NetworkGraph_OnRefresh(object? sender, long e)
        {
            Logger!.LogInformation(Context!.NetworkGraph!.Counters.Dump());
        }

        protected override void Finish()
        {
            Context!.NetworkGraph!.StopPeriodicRefresh();
        }
    }
}
