﻿using EntityFX.MqttY.Contracts.Monitoring;

internal class TextFileMonitoringProvider : MonitoringProviderBase, IMonitoringProvider, IDisposable
{
    private bool disposedValue;
    private readonly StreamWriter textWriter;

    public TextFileMonitoringProvider(IMonitoring monitoring, string filePath)
        : base(monitoring)
    {
        textWriter = new StreamWriter(filePath, true, System.Text.Encoding.UTF8, 4096);
    }

    protected override void WriteScope(MonitoringScope scope)
    {
        textWriter.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: " +
            $"Begin Scope <{scope?.Id}> (StartTick={scope?.StartTick}, Ticks={scope?.Ticks}): \"{scope?.ScopeLabel}\"");


        if (scope?.Items?.Any() == true)
        {
            foreach (var item in scope.Items.ToArray())
            {
                if (item.MonitoringItemType == MonitoringItemType.Scope)
                {
                    WriteScope((MonitoringScope)item);
                }
                else
                {
                    WriteItem((MonitoringItem)item);
                }
            }
        }

        textWriter.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: End Scope <{scope?.Id}>: \"{scope?.ScopeLabel}\"");
        textWriter.WriteLine();
    }

    protected override void WriteItem(MonitoringItem item)
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
