﻿using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Network;

namespace EntityFX.MqttY.Scenarios
{
    internal class NetworkInitAction : ScenarioAction<NetworkSimulation, NetworkGraphFactoryOption>
    {
        public override Task ExecuteAsync()
        {
            if (Config == null)
            {
                throw new ArgumentNullException(nameof(Config));
            }

            if (Config.MonitoringOption.Path != null)
            {
                Config.MonitoringOption.Path = Config.MonitoringOption.Path
                    .Replace("{scenario}", Scenario.Name)
                    .Replace("{date}", $"{DateTime.Now:yyyy_MM_dd__HH_mm}");
            }

            Context!.NetworkGraph = Config.NetworkGraphFactory!.Create(Config);
            Context!.NetworkGraph.OnRefresh += NetworkGraph_OnRefresh;
            Context!.NetworkGraph!.StartPeriodicRefreshAsync();

            Config.NetworkSimulatorBuilder!.OptionsPath = Config.OptionsPath;
            Config.NetworkSimulatorBuilder!.Configure(Context!.NetworkGraph, Config.NetworkGraphOption);

            return Task.CompletedTask;
        }

        private void NetworkGraph_OnRefresh(object? sender, long e)
        {
            Console.Write(Context!.NetworkGraph!.Counters.Dump());
        }

        protected override void Finish()
        {
            Context!.NetworkGraph!.StopPeriodicRefresh();
        }
    }
}
