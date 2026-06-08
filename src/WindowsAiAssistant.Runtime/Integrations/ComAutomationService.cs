using System.Reflection;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class ComAutomationService : IComAutomationService
{
    private readonly ComAutomationOptions _options;

    public ComAutomationService(RuntimeOptions runtimeOptions)
    {
        ArgumentNullException.ThrowIfNull(runtimeOptions);
        _options = runtimeOptions.ComAutomation ?? new ComAutomationOptions();
        if (_options.AllowedOperations.Count == 0)
        {
            _options.AllowedOperations = ComAllowlistDefaults.CreateDefault();
        }
    }

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

        var trimmedProgId = progId.Trim();
        var trimmedMethod = method.Trim();

        if (!ComAllowlistValidator.IsAllowed(trimmedProgId, trimmedMethod, _options.AllowedOperations))
        {
            return new ActionResult
            {
                Success = false,
                Message = $"COM islemi allowlist disinda: progId={trimmedProgId}, method={trimmedMethod}"
            };
        }

        object? comObject = null;
        var createdNewInstance = false;
        try
        {
            var type = Type.GetTypeFromProgID(trimmedProgId, throwOnError: false);
            if (type is null)
            {
                return new ActionResult { Success = false, Message = $"ProgID bulunamadi: {trimmedProgId}" };
            }

            comObject = ComInstanceResolver.TryGetRunningInstance(trimmedProgId);
            if (comObject is null)
            {
                comObject = Activator.CreateInstance(type);
                createdNewInstance = comObject is not null;
            }

            if (comObject is null)
            {
                return new ActionResult { Success = false, Message = $"COM ornegi olusturulamadi: {trimmedProgId}" };
            }

            var args = ParseArguments(arguments);
            var result = type.InvokeMember(
                trimmedMethod,
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                target: comObject,
                args: args);

            var message = new StringBuilder()
                .AppendLine($"progId={trimmedProgId}")
                .AppendLine($"method={trimmedMethod}")
                .AppendLine($"reusedInstance={!createdNewInstance}")
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
            if (closeInstance && comObject is not null && createdNewInstance)
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
