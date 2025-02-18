namespace EntityFX.MqttY.Contracts.Monitoring;

public record MonitoringScope(Guid Id, string Name, int Level, DateTimeOffset Date, List<MonitoringItem> Items);