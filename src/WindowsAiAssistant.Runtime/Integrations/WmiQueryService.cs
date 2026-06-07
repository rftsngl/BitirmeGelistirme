using System.Management;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class WmiQueryService : IWmiQueryService
{
    private const int MaxRows = 40;
    private const int MaxChars = 4000;

    public ActionResult Query(string wql, string? wmiNamespace = null)
    {
        if (string.IsNullOrWhiteSpace(wql))
        {
            return new ActionResult { Success = false, Message = "wmi_query icin target veya parameters.query gerekli." };
        }

        var scope = string.IsNullOrWhiteSpace(wmiNamespace) ? @"root\cimv2" : wmiNamespace.Trim();

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine($"namespace={scope}");
            builder.AppendLine($"query={wql.Trim()}");

            using var searcher = new ManagementObjectSearcher(scope, wql.Trim());
            var rows = 0;
            foreach (ManagementObject item in searcher.Get())
            {
                rows++;
                if (rows > MaxRows)
                {
                    builder.AppendLine($"... truncated after {MaxRows} rows");
                    break;
                }

                builder.AppendLine($"--- row {rows} ---");
                foreach (var property in item.Properties)
                {
                    if (property.Value is null)
                    {
                        continue;
                    }

                    builder.AppendLine($"{property.Name}={FormatValue(property.Value)}");
                }

                item.Dispose();
            }

            if (rows == 0)
            {
                builder.AppendLine("(no rows)");
            }

            var text = Truncate(builder.ToString());
            return new ActionResult
            {
                Success = true,
                Message = text
            };
        }
        catch (Exception ex)
        {
            return new ActionResult
            {
                Success = false,
                Message = $"WMI sorgusu basarisiz: {ex.Message}"
            };
        }
    }

    private static string FormatValue(object value) =>
        value switch
        {
            Array array => string.Join(",", array.Cast<object>().Select(FormatValue)),
            _ => value.ToString()?.Replace('\n', ' ').Replace('\r', ' ') ?? string.Empty
        };

    private static string Truncate(string text) =>
        text.Length <= MaxChars ? text : text[..MaxChars] + "...";
}
