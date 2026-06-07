using System.Diagnostics;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class PerformanceCounterService : IPerformanceCounterService
{
    public ActionResult Execute(string mode = "snapshot")
    {
        if (!(mode ?? "snapshot").Equals("snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return IntegrationResultHelper.Fail("Desteklenen mod: snapshot.");
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine($"timestamp={DateTimeOffset.Now:O}");

            var cpu = GetCpuUsage();
            if (cpu is not null)
            {
                builder.AppendLine($"cpuPercent={cpu:F1}");
            }

            var proc = Process.GetCurrentProcess();
            var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var usedMemory = Process.GetProcesses().Sum(p =>
            {
                try
                {
                    return p.WorkingSet64;
                }
                catch
                {
                    return 0L;
                }
                finally
                {
                    p.Dispose();
                }
            });

            builder.AppendLine($"workingSetMB={proc.WorkingSet64 / (1024 * 1024)}");
            builder.AppendLine($"processCount={Process.GetProcesses().Length}");
            builder.AppendLine($"approxUsedMemoryGB={usedMemory / (1024d * 1024 * 1024):F2}");
            builder.AppendLine($"availableMemoryGB={totalMemory / (1024d * 1024 * 1024):F2}");

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var freePercent = drive.TotalSize > 0 ? drive.AvailableFreeSpace * 100d / drive.TotalSize : 0;
                builder.AppendLine($"disk {drive.Name} free={freePercent:F1}% ({drive.AvailableFreeSpace / (1024 * 1024 * 1024)} GB)");
            }

            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Performans olcumu alinamadi: {ex.Message}");
        }
    }

    private static float? GetCpuUsage()
    {
        try
        {
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();
            Thread.Sleep(250);
            return cpuCounter.NextValue();
        }
        catch
        {
            return null;
        }
    }
}
