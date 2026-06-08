using Windows.UI.StartScreen;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Integrations;

namespace WindowsAiAssistant.App.Integrations;

public sealed class WinJumpListService : IJumpListService
{
    public async Task<ActionResult> UpdateAsync(
        string mode,
        string? tasks = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = (mode ?? "set").Trim().ToLowerInvariant();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppNotificationIdentity.EnsureRegistered();

            if (normalized is "clear" or "reset")
            {
                var current = await JumpList.LoadCurrentAsync().AsTask().ConfigureAwait(false);
                current.Items.Clear();
                await current.SaveAsync().AsTask().ConfigureAwait(false);
                return new ActionResult { Success = true, Message = "Jump List temizlendi." };
            }

            var jumpList = await JumpList.LoadCurrentAsync().AsTask().ConfigureAwait(false);
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

            await jumpList.SaveAsync().AsTask().ConfigureAwait(false);
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
                ErrorCode = ActionFailureCodes.HandlerException,
                ExceptionType = ex.GetType().Name,
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
