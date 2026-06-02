using EntityFX.MqttY.Contracts.NetworkLogger;

namespace EntityFX.MqttY.Designer.Services;

/// <summary>
/// Writes simulation log entries to a file for debugging.
/// Subscribes to INetworkLogger.Added and writes each entry to a rolling log file.
/// </summary>
public class FileLogService : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private bool _disposed;

    public FileLogService(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MqttY", "logs");

        Directory.CreateDirectory(_logDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _logFilePath = Path.Combine(_logDirectory, $"simulation-{timestamp}.log");
    }

    public string LogFilePath => _logFilePath;

    public void Start(INetworkLogger logger)
    {
        lock (_lock)
        {
            if (_disposed) return;

            _writer = new StreamWriter(_logFilePath, append: false) { AutoFlush = true };
            _writer.WriteLine($"=== MqttY Simulation Log started at {DateTime.Now:u} ===");
            _writer.WriteLine($"=== Log file: {_logFilePath} ===");
            _writer.WriteLine();
        }

        logger.Added += OnLogItemAdded;
    }

    public void Stop(INetworkLogger logger)
    {
        logger.Added -= OnLogItemAdded;

        lock (_lock)
        {
            _writer?.WriteLine();
            _writer?.WriteLine($"=== MqttY Simulation Log stopped at {DateTime.Now:u} ===");
            _writer?.Close();
            _writer = null;
        }
    }

    private void OnLogItemAdded(object? sender, NetworkLoggerItem item)
    {
        lock (_lock)
        {
            if (_writer == null || _disposed) return;

            try
            {
                _writer.WriteLine(
                    $"[T:{item.Tick,6}] {item.Date:HH:mm:ss.fff} | " +
                    $"{item.Type,-10} | " +
                    $"{item.From,-20} -> {item.To,-20} | " +
                    $"{item.Protocol,-8} | " +
                    $"{item.Message}");
            }
            catch
            {
                // Ignore write errors
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _writer?.Close();
            _writer = null;
        }
    }
}