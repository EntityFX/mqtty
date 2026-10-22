using System.Globalization;

namespace EntityFX.MqttY.LoadBalancing;

/// <summary>Renders model results without ANSI escapes, GUI or plotting dependencies.</summary>
public static class DemoConsoleView
{
    public static void WriteIntroduction(TextWriter writer, LoadBalancingOptions options)
    {
        writer.WriteLine("Демонстрация MqttY: идеальные модельные брокеры, без реального профиля.");
        writer.WriteLine($"Режим: {options.Mode}; брокеров: {options.BackendCount}; publishers: {options.TotalPublishers}; QoS: {options.PublishQos}; seed: {options.Seed}.");
        writer.WriteLine($"Нагрузка: {Number(options.OfferedRps)} сообщений/с; измерение: {Number(options.MeasurementSeconds)} модельных с.");
        writer.WriteLine("Publisher -> выбор backend перед CONNECT -> его брокер -> его receiver.");
        writer.Flush();
    }

    public static void WriteResult(TextWriter writer, PreparedExperiment input, ExperimentSummary summary, string? output)
    {
        writer.WriteLine();
        writer.WriteLine("Назначенные publisher-соединения (receivers не включены):");
        writer.WriteLine("Длина полосы: относительно максимума среди брокеров в каждой диаграмме.");
        var assigned = Enumerable.Range(0, input.Options.BackendCount)
            .Select(backend => input.Assignments.Count(n => n == backend)).ToArray();
        var maximum = assigned.Max();
        for (var backend = 0; backend < assigned.Length; backend++)
            writer.WriteLine($"  broker{backend,-2} {Bar(assigned[backend], maximum)} {assigned[backend]}");

        writer.WriteLine();
        writer.WriteLine("Первые корректные получения измерительных ID, включая drain:");
        var mostDelivered = summary.Backends.Count == 0 ? 0 : summary.Backends.Max(b => b.UniqueReceived);
        foreach (var backend in summary.Backends)
            writer.WriteLine($"  broker{backend.Backend,-2} {Bar(backend.UniqueReceived, mostDelivered)} {backend.UniqueReceived}  ({Number(backend.UniqueReceived / summary.MeasurementSeconds)} сообщений/с)");
        writer.WriteLine();
        writer.WriteLine($"Статус: {summary.Status}; ошибок графа: {summary.GraphErrors}.");
        writer.WriteLine($"Запланировано: {summary.ScheduledPublishes}; вызовов Publish: {summary.ApiAttempts}; не отправлено: {summary.BackpressureUnsent}; немедленных отказов: {summary.SendRejected}.");
        writer.WriteLine($"Уникально получено: {summary.Delivery.UniqueDelivered}; из них в drain: {summary.UniqueReceivedDuringDrain}; дубликатов: {summary.Delivery.DuplicateDeliveries}; неверный backend: {summary.Delivery.UnexpectedDeliveries}; неизвестных ID: {summary.Delivery.UnknownDeliveries}.");
        writer.WriteLine($"Завершения backend: {Number(summary.BackendCompletedRps)} сообщений/с; unique goodput: {Number(summary.UniqueGoodput)} сообщений/с.");
        writer.WriteLine($"Недоставка среди вызовов Publish: {Number(summary.AttemptDeliveryLoss)} (доля).");
        writer.WriteLine($"p99 завершения публикации: {Number(summary.PublishP99Ms)} мс; p99 первого получения: {Number(summary.ReceiverP99Ms)} мс.");
        writer.WriteLine("Скорости включают drain измерительной когорты и делятся на время нагрузки; прогрев исключён.");
        if (input.Options.PublishQos == 0) writer.WriteLine("QoS 0: задержка ACK неприменима.");
        if (summary.Error != null) writer.WriteLine("Причина отказа: " + summary.Error.Split('\n')[0].TrimEnd());
        writer.WriteLine("Одинаковые постоянные сессии: directBalanced, Round Robin и Least Connections могут дать одинаковые результаты.");
        writer.WriteLine("Это проверка механизма; точность настоящего брокера или TCP-балансировщика здесь не оценивается.");
        writer.WriteLine(output == null ? "Файлы не записаны. Для сохранения: --output <новый-каталог>." : "Пакет результатов: " + output);
    }

    private static string Bar(long value, long maximum)
    {
        const int width = 20;
        var filled = maximum == 0 || value == 0 ? 0 : Math.Max(1, (int)Math.Round((double)value / maximum * width));
        return "[" + new string('#', filled) + new string('.', width - filled) + "]";
    }

    private static string Number(double? value) => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? "notApplicable";
}
