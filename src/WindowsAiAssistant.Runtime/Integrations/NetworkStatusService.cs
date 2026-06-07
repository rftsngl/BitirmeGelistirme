using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class NetworkStatusService : INetworkStatusService
{
    public ActionResult Execute(string mode = "status")
    {
        var normalized = (mode ?? "status").Trim().ToLowerInvariant();
        return normalized switch
        {
            "status" => BuildStatus(),
            "adapters" => BuildAdapters(),
            _ => IntegrationResultHelper.Fail($"Desteklenen modlar: status, adapters. Verilen: {mode}")
        };
    }

    private static ActionResult BuildStatus()
    {
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine($"networkAvailable={NetworkInterface.GetIsNetworkAvailable()}");
            builder.AppendLine($"internet={HasInternet()}");
            builder.AppendLine($"host={Dns.GetHostName()}");

            foreach (var address in Dns.GetHostAddresses(Dns.GetHostName())
                         .Where(a => a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6)
                         .Take(8))
            {
                builder.AppendLine($"address={address}");
            }

            var active = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                     n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
            if (active is not null)
            {
                builder.AppendLine($"activeAdapter={active.Name}");
                builder.AppendLine($"activeType={active.NetworkInterfaceType}");
                var ip = active.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ip is not null)
                {
                    builder.AppendLine($"ipv4={ip.Address}");
                }

                var dns = active.GetIPProperties().DnsAddresses.FirstOrDefault();
                if (dns is not null)
                {
                    builder.AppendLine($"dns={dns}");
                }
            }

            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Ag durumu okunamadi: {ex.Message}");
        }
    }

    private static ActionResult BuildAdapters()
    {
        try
        {
            var builder = new StringBuilder();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                builder.AppendLine("---");
                builder.AppendLine($"name={adapter.Name}");
                builder.AppendLine($"description={adapter.Description}");
                builder.AppendLine($"status={adapter.OperationalStatus}");
                builder.AppendLine($"type={adapter.NetworkInterfaceType}");
                builder.AppendLine($"speed={adapter.Speed}");

                foreach (var address in adapter.GetIPProperties().UnicastAddresses.Take(4))
                {
                    builder.AppendLine($"ip={address.Address}");
                }
            }

            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Ag adaptorleri okunamadi: {ex.Message}");
        }
    }

    private static bool HasInternet()
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync("1.1.1.1", 443);
            return task.Wait(1500) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
