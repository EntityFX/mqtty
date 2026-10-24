namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Реестр заданий симуляций.</summary>
public interface IJobRegistry
{
    void Add(SimulationJob job);

    SimulationJob? Get(string id);

    IReadOnlyList<SimulationJob> GetAll();

    void Remove(string id);
}
