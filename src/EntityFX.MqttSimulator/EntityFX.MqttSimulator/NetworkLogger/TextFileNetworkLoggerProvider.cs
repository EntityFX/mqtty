using EntityFX.MqttY.Contracts.NetworkLogger;

internal class TextFileNetworkLoggerProvider : NetworkLoggerBase, IINetworkLoggerProvider, IDisposable
{
    private bool disposedValue;
    private readonly StreamWriter textWriter;

    public TextFileNetworkLoggerProvider(INetworkLogger monitoring, string filePath)
        : base(monitoring)
    {
        textWriter = new StreamWriter(filePath, true, System.Text.Encoding.UTF8, 4096);
    }

    protected override void WriteScope(NetworkLoggerScope scope)
    {
        textWriter.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: " +
            $"Begin Scope <{scope?.Id}> (StartTick={scope?.StartTick}, TotalTicks={scope?.Ticks}): \"{scope?.ScopeLabel}\"");


        if (scope?.Items?.Any() == true)
        {
            foreach (var item in scope.Items.ToArray())
            {
                if (item.ItemType == NetworkLoggerItemType.Scope)
                {
                    WriteScope((NetworkLoggerScope)item);
                }
                else
                {
                    WriteItem((NetworkLoggerItem)item);
                }
            }
        }

        textWriter.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: End Scope <{scope?.Id}>: \"{scope?.ScopeLabel}\"");
        textWriter.WriteLine();
    }

    protected override void WriteItem(NetworkLoggerItem item)
    {
        textWriter.WriteLine(GetMonitoringLine(item));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
