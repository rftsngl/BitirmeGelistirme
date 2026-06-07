using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public static class IntegrationResultHelper
{
    internal const int MaxChars = 4000;

    public static ActionResult Ok(StringBuilder builder) =>
        Ok(builder.ToString());

    public static ActionResult Ok(string message) =>
        new() { Success = true, Message = Truncate(message) };

    public static ActionResult Fail(string message) =>
        new() { Success = false, Message = Truncate(message) };

    public static string Truncate(string text) =>
        text.Length <= MaxChars ? text : text[..MaxChars] + "...";
}
