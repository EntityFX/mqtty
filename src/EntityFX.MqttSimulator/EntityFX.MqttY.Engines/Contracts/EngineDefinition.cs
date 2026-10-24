namespace EntityFX.MqttY.Engines.Contracts;

/// <summary>Описание движка: имя, назначение и поддерживаемые команды.</summary>
public sealed class EngineDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<EngineCommand> Commands { get; init; } = Array.Empty<EngineCommand>();
}
