using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Web.App.Models;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Генератор топологий по параметрам (line/ring/star/combined).</summary>
public interface ITopologyGeneratorService
{
    NetworkGraphOption Generate(TopologySpecDto spec);
}
