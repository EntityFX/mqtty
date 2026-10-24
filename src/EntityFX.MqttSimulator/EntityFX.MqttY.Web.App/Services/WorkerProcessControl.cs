using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Приостанавливает только живой worker текущего задания, сохраняя состояние модели в памяти.</summary>
internal static class WorkerProcessControl
{
    public static void SetPaused(SimulationJob job, bool paused)
    {
        if (job.ProcessId is not int pid || job.Cts == null)
            throw new InvalidOperationException("Worker ещё не запущен или уже завершён.");
        using var process = FindWorker(pid);
        if (process.HasExited) throw new InvalidOperationException("Worker уже завершён.");
        if (OperatingSystem.IsLinux())
        {
            if (Kill(pid, paused ? 19 : 18) != 0) throw new InvalidOperationException("Не удалось изменить состояние worker.");
        }
        else if (OperatingSystem.IsWindows())
        {
            var status = paused ? NtSuspendProcess(process.Handle) : NtResumeProcess(process.Handle);
            if (status != 0) throw new InvalidOperationException("Не удалось изменить состояние worker: " + status);
        }
        else throw new InvalidOperationException("Пауза worker поддерживается на Linux и Windows.");
    }

    private static Process FindWorker(int pid)
    {
        try { return Process.GetProcessById(pid); }
        catch (ArgumentException error) { throw new InvalidOperationException("Worker уже завершён.", error); }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr process);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr process);
}
