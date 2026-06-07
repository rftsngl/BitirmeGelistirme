using System.Reflection;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class ComAutomationService : IComAutomationService
{
    public ActionResult Invoke(string progId, string method, string? arguments = null, bool closeInstance = false)
    {
        if (string.IsNullOrWhiteSpace(progId) || string.IsNullOrWhiteSpace(method))
        {
            return new ActionResult
            {
                Success = false,
                Message = "com_invoke icin parameters.progId ve parameters.method gerekli."
            };
        }

        object? comObject = null;
        try
        {
            var type = Type.GetTypeFromProgID(progId.Trim(), throwOnError: false);
            if (type is null)
            {
                return new ActionResult { Success = false, Message = $"ProgID bulunamadi: {progId}" };
            }

            comObject = Activator.CreateInstance(type);
            if (comObject is null)
            {
                return new ActionResult { Success = false, Message = $"COM ornegi olusturulamadi: {progId}" };
            }

            var args = ParseArguments(arguments);
            var result = type.InvokeMember(
                method.Trim(),
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                target: comObject,
                args: args);

            var message = new StringBuilder()
                .AppendLine($"progId={progId.Trim()}")
                .AppendLine($"method={method.Trim()}")
                .AppendLine($"result={FormatResult(result)}")
                .ToString();

            return new ActionResult { Success = true, Message = message.Trim() };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"COM cagrisi basarisiz: {ex.Message}" };
        }
        finally
        {
            if (closeInstance && comObject is not null)
            {
                TryQuit(comObject);
                ReleaseComObject(comObject);
            }
        }
    }

    private static object?[] ParseArguments(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<object?>();
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<object?[]>(trimmed);
                return parsed ?? Array.Empty<object?>();
            }
            catch
            {
                // fall through to CSV
            }
        }

        return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => (object?)token)
            .ToArray();
    }

    private static string FormatResult(object? result) =>
        result switch
        {
            null => "(null)",
            Array array => string.Join(",", array.Cast<object?>().Select(static v => v?.ToString() ?? "null")),
            _ => result.ToString() ?? "(empty)"
        };

    private static void TryQuit(object comObject)
    {
        try
        {
            comObject.GetType().InvokeMember(
                "Quit",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null,
                comObject,
                Array.Empty<object?>());
        }
        catch
        {
            // optional
        }
    }

    private static void ReleaseComObject(object comObject)
    {
        try
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
            // best effort
        }
    }
}
