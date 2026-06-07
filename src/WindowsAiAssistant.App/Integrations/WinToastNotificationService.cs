using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Integrations;

namespace WindowsAiAssistant.App.Integrations;

public sealed class WinToastNotificationService : IToastNotificationService
{
    public ActionResult Show(string title, string body)
    {
        try
        {
            AppNotificationIdentity.EnsureRegistered();

            var safeTitle = EscapeXml(title);
            var safeBody = EscapeXml(body);
            var xml = $"""
                <toast activationType="foreground">
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{safeTitle}</text>
                      <text>{safeBody}</text>
                    </binding>
                  </visual>
                </toast>
                """;

            var document = new XmlDocument();
            document.LoadXml(xml);
            var notification = new ToastNotification(document);
            ToastNotificationManager.CreateToastNotifier(AppNotificationIdentity.AppUserModelId).Show(notification);

            return new ActionResult
            {
                Success = true,
                Message = $"Toast gosterildi: {title}"
            };
        }
        catch (Exception ex)
        {
            return new ActionResult
            {
                Success = false,
                Message = $"Toast gosterilemedi: {ex.Message}"
            };
        }
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
}
