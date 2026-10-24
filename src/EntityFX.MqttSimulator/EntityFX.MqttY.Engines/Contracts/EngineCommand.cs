namespace EntityFX.MqttY.Engines.Contracts;

/// <summary>Команды движка, совместимые по смыслу с командами существующих CLI-приложений.</summary>
public enum EngineCommand
{
    /// <summary>Выполнить одиночный эксперимент.</summary>
    Run,

    /// <summary>Проверить входные данные без исполнения.</summary>
    Validate,

    /// <summary>Демонстрационный запуск без сохранения результатов.</summary>
    Demo,

    /// <summary>Выполнить матрицу экспериментов.</summary>
    Matrix
}
