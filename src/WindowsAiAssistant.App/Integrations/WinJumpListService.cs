using Windows.UI.StartScreen;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Integrations;

namespace WindowsAiAssistant.App.Integrations;

public sealed class WinJumpListService : IJumpListService
{
    public ActionResult Update(string mode, string? tasks = null)
    {
        var normalized = (mode ?? "set").Trim().ToLowerInvariant();
        try
        {
            AppNotificationIdentity.EnsureRegistered();

            if (normalized is "clear" or "reset")
            {
                var current = JumpList.LoadCurrentAsync().AsTask().GetAwaiter().GetResult();
                current.Items.Clear();
                current.SaveAsync().AsTask().GetAwaiter().GetResult();
                return new ActionResult { Success = true, Message = "Jump List temizlendi." };
            }

            var jumpList = JumpList.LoadCurrentAsync().AsTask().GetAwaiter().GetResult();
            jumpList.Items.Clear();

            var entries = ParseTasks(tasks);
            foreach (var entry in entries)
            {
                var parts = entry.Split('|', 2, StringSplitOptions.TrimEntries);
                var title = parts[0];
                var args = parts.Length > 1 ? parts[1] : title;
                var item = JumpListItem.CreateWithArguments(args, title);
                jumpList.Items.Add(item);
            }

            jumpList.SaveAsync().AsTask().GetAwaiter().GetResult();
            return new ActionResult
            {
                Success = true,
                Message = $"Jump List guncellendi ({jumpList.Items.Count} oge)."
            };
        }
        catch (Exception ex)
        {
            return new ActionResult
            {
                Success = false,
                Message = $"Jump List guncellenemedi: {ex.Message}"
            };
        }
    }

    private static IReadOnlyList<string> ParseTasks(string? tasks)
    {
        if (string.IsNullOrWhiteSpace(tasks))
        {
            return
            [
                "Son komut|--jump last",
                "Ayarlar|--jump settings"
            ];
        }

        return tasks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
