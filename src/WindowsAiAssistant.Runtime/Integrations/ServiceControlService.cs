using System.ServiceProcess;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class ServiceControlService : IServiceControlService
{
    public ActionResult Execute(string mode, string? serviceName = null)
    {
        var normalized = (mode ?? "list").Trim().ToLowerInvariant();
        return normalized switch
        {
            "list" => ListServices(serviceName),
            "status" => GetStatus(serviceName),
            "start" => ControlService(serviceName, s => { s.Start(); s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)); }, "baslatildi"),
            "stop" => ControlService(serviceName, s => { s.Stop(); s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); }, "durduruldu"),
            "restart" => RestartService(serviceName),
            _ => IntegrationResultHelper.Fail($"Desteklenen modlar: list, status, start, stop, restart. Verilen: {mode}")
        };
    }

    private static ActionResult ListServices(string? filter)
    {
        try
        {
            var builder = new StringBuilder();
            var services = ServiceController.GetServices()
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase);

            var count = 0;
            foreach (var service in services)
            {
                using (service)
                {
                    if (!MatchesFilter(service, filter))
                    {
                        continue;
                    }

                    count++;
                    builder.AppendLine($"{service.ServiceName} | {service.DisplayName} | {service.Status}");
                }
            }

            builder.Insert(0, $"count={count}\n");
            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Servis listesi alinamadi: {ex.Message}");
        }
    }

    private static ActionResult GetStatus(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return IntegrationResultHelper.Fail("status icin target veya parameters.name gerekli.");
        }

        try
        {
            using var service = ResolveService(serviceName);
            if (service is null)
            {
                return IntegrationResultHelper.Fail($"Servis bulunamadi: {serviceName}");
            }

            var builder = new StringBuilder();
            builder.AppendLine($"name={service.ServiceName}");
            builder.AppendLine($"display={service.DisplayName}");
            builder.AppendLine($"status={service.Status}");
            builder.AppendLine($"canStop={service.CanStop}");
            builder.AppendLine($"canPauseAndContinue={service.CanPauseAndContinue}");
            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Servis durumu okunamadi: {ex.Message}");
        }
    }

    private static ActionResult ControlService(string? serviceName, Action<ServiceController> action, string verb)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return IntegrationResultHelper.Fail($"Servis adi gerekli ({verb}).");
        }

        try
        {
            using var service = ResolveService(serviceName);
            if (service is null)
            {
                return IntegrationResultHelper.Fail($"Servis bulunamadi: {serviceName}");
            }

            action(service);
            service.Refresh();
            return IntegrationResultHelper.Ok($"{service.ServiceName} {verb}. status={service.Status}");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Servis islemi basarisiz: {ex.Message}");
        }
    }

    private static ActionResult RestartService(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return IntegrationResultHelper.Fail("restart icin servis adi gerekli.");
        }

        try
        {
            using var service = ResolveService(serviceName);
            if (service is null)
            {
                return IntegrationResultHelper.Fail($"Servis bulunamadi: {serviceName}");
            }

            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            service.Refresh();
            return IntegrationResultHelper.Ok($"{service.ServiceName} yeniden baslatildi. status={service.Status}");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Servis yeniden baslatilamadi: {ex.Message}");
        }
    }

    private static ServiceController? ResolveService(string serviceName)
    {
        var trimmed = serviceName.Trim();
        var exact = ServiceController.GetServices()
            .FirstOrDefault(s => s.ServiceName.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return ServiceController.GetServices()
            .FirstOrDefault(s => s.DisplayName.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesFilter(ServiceController service, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return service.ServiceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               service.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
