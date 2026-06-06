namespace WindowsAiAssistant.Runtime.Config;

public sealed class UiAutomationOptions
{
    public int MaxDepth { get; set; } = 4;
    public int MaxElementsPerStep { get; set; } = 80;
    public int CaptureTimeoutMs { get; set; } = 8000;

    /// <summary>
    /// Chromium/Electron pencerelerinde erisilebilirligi uyandirmak icin WM_GETOBJECT
    /// gonderir. Kapatilirsa bu uygulamalarda UI agaci bos kalabilir.
    /// </summary>
    public bool EnableChromiumAccessibility { get; set; } = true;

    /// <summary>
    /// UIA3 capture zayif kalirsa (eski WinForms/Win32) UIA2 motoruyla yeniden dener.
    /// </summary>
    public bool EnableUia2Fallback { get; set; } = true;

    /// <summary>
    /// UIA3 bu sayidan az element bulursa UIA2 fallback denenir.
    /// </summary>
    public int Uia2FallbackMinElements { get; set; } = 3;
}
