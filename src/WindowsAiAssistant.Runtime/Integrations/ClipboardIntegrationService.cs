using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class ClipboardIntegrationService : IClipboardIntegrationService
{
    public ActionResult Execute(string mode, string? text = null)
    {
        var normalized = (mode ?? "read").Trim().ToLowerInvariant();
        return normalized switch
        {
            "read" => Read(),
            "write" or "set" => Write(text),
            "clear" => Clear(),
            _ => IntegrationResultHelper.Fail($"Desteklenen modlar: read, write, clear. Verilen: {mode}")
        };
    }

    private static ActionResult Read()
    {
        try
        {
            if (!System.Windows.Forms.Clipboard.ContainsText())
            {
                return IntegrationResultHelper.Ok("(panoda metin yok)");
            }

            var content = System.Windows.Forms.Clipboard.GetText();
            return IntegrationResultHelper.Ok(content);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Pano okunamadi: {ex.Message}");
        }
    }

    private static ActionResult Write(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return IntegrationResultHelper.Fail("write icin parameters.text gerekli.");
        }

        try
        {
            System.Windows.Forms.Clipboard.SetText(text);
            return IntegrationResultHelper.Ok($"Panoya yazildi ({text.Length} karakter).");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Panoya yazilamadi: {ex.Message}");
        }
    }

    private static ActionResult Clear()
    {
        try
        {
            System.Windows.Forms.Clipboard.Clear();
            return IntegrationResultHelper.Ok("Pano temizlendi.");
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Pano temizlenemedi: {ex.Message}");
        }
    }
}
