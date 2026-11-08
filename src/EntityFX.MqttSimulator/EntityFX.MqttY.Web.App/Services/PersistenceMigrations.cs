using EntityFX.MqttY.Web.App.Models;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>
/// Versioned persistence migrations. V1 documents have no schemaVersion (or 1);
/// they are upgraded in memory to V2 after deserialization.
/// </summary>
public static class PersistenceMigrations
{
    public const int CurrentVersion = 2;

    public static T Normalize<T>(T? value)
    {
        switch (value)
        {
            case SavedTopologyDto saved:
                saved.SchemaVersion = CurrentVersion;
                break;
            case SimulationJob job:
                job.SchemaVersion = CurrentVersion;
                break;
            case MonitoringSnapshotDto snapshot:
                snapshot.SchemaVersion = CurrentVersion;
                break;
        }

        return value!;
    }
}
