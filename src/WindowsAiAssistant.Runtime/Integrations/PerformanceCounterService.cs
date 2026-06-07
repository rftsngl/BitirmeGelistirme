using System.Diagnostics;
using System.Runtime.InteropServices;
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
            builder.AppendLine($"assistantWorkingSetMB={proc.WorkingSet64 / (1024 * 1024)}");

            if (TryGetMemoryStatus(out var totalPhysMb, out var availPhysMb, out var usedPercent))
            {
                builder.AppendLine($"totalMemoryGB={totalPhysMb / 1024d:F2}");
                builder.AppendLine($"availableMemoryGB={availPhysMb / 1024d:F2}");
                builder.AppendLine($"memoryUsedPercent={usedPercent:F1}");
            }

            var processes = Process.GetProcesses();
            builder.AppendLine($"processCount={processes.Length}");
            long workingSetSum = 0;
            foreach (var p in processes)
            {
                try
                {
                    workingSetSum += p.WorkingSet64;
                }
                catch
                {
                    // ignore inaccessible processes
                }
                finally
                {
                    p.Dispose();
                }
            }

            builder.AppendLine(
                $"workingSetSumGB={workingSetSum / (1024d * 1024 * 1024):F2} (approx; shared pages may be counted more than once)");

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var freePercent = drive.TotalSize > 0 ? drive.AvailableFreeSpace * 100d / drive.TotalSize : 0;
                builder.AppendLine(
                    $"disk {drive.Name} free={freePercent:F1}% ({drive.AvailableFreeSpace / (1024 * 1024 * 1024)} GB)");
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

    private static bool TryGetMemoryStatus(out double totalPhysMb, out double availPhysMb, out double usedPercent)
    {
        totalPhysMb = availPhysMb = usedPercent = 0;
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return false;
        }

        totalPhysMb = status.TotalPhys / (1024d * 1024);
        availPhysMb = status.AvailPhys / (1024d * 1024);
        usedPercent = status.MemoryLoad;
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
