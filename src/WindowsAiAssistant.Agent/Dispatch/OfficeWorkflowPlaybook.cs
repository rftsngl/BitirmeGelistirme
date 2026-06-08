using System.Text;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

internal static class OfficeWorkflowPlaybook
{
    internal static void AppendIfRelevant(StringBuilder builder, string userGoal, DesktopObservation observation)
    {
        if (!IsWordRelatedGoal(userGoal))
        {
            return;
        }

        var wordWindows = observation.Windows
            .Where(window => window.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase))
            .ToList();

        builder.AppendLine("SUGGESTED — WORD / OFFICE WORKFLOW (you decide):");
        if (wordWindows.Count > 0)
        {
            var sample = wordWindows[0];
            builder.AppendLine($"- Word zaten acik ({wordWindows.Count} pencere). open_app KULLANMA; focus_window target={sample.WindowId} veya baslik parcasi.");
            builder.AppendLine("- Yeni bos belge: com_invoke progId=Word.Application method=Documents.Add (mevcut Word ornegini yeniden kullanir) VEYA press_shortcut Ctrl+N.");
            builder.AppendLine("- Word baslangic ekraninda 'Bos belge' tiklamak yerine Documents.Add veya Ctrl+N tercih et (daha az adim).");
        }
        else
        {
            builder.AppendLine("- Word acik degilse: open_app target=word, sonra wait (UI yuklensin), sonra focus_window.");
            builder.AppendLine("- Bos belge: com_invoke Documents.Add veya Ctrl+N — start ekraninda gereksiz click_element kullanma.");
        }

        builder.AppendLine("- Ayni gorevde ikinci kez open_app veya ikinci bos belge penceresi ACMA; once actigin belge penceresinde devam et.");
        if (MentionsHeadingOrTitle(userGoal))
        {
            builder.AppendLine("- Baslik olusturma: focus_window belge penceresine, type_text ile metni yaz; Heading 1 icin metni secip press_shortcut Ctrl+Alt+1.");
        }

        builder.AppendLine();
    }

    private static bool IsWordRelatedGoal(string userGoal)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return false;
        }

        var text = FastPathGuard.Normalize(userGoal);
        return FastPathGuard.MatchesAny(text,
            "word", "winword", "microsoft word", "belge", "belgesi", "belgeye", "bos belge", "boş belge");
    }

    private static bool MentionsHeadingOrTitle(string userGoal)
    {
        var text = FastPathGuard.Normalize(userGoal);
        return FastPathGuard.MatchesAny(text,
            "baslik", "başlık", "title", "heading", "adli", "adlı");
    }
}
