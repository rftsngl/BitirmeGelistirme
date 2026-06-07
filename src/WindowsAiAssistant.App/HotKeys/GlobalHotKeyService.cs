using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.HotKeys;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int HotKeyId = 9001;
    private readonly AudioOptions _options;
    private NativeMessageWindow? _messageWindow;
    private bool _registered;

    public GlobalHotKeyService(AudioOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public event EventHandler? HotKeyPressed;

    public void Start()
    {
        Restart();
    }

    /// <summary>
    /// Mevcut kaydi kaldir ve seceneklere gore yeniden kaydet. Hata mesaji veya null.
    /// </summary>
    public string? Restart()
    {
        Stop();

        if (!_options.GlobalHotKeyEnabled)
        {
            return null;
        }

        if (!TryParseHotKey(_options.GlobalHotKey, out var modifiers, out var virtualKey))
        {
            return $"Gecersiz kisayol: '{_options.GlobalHotKey}'. Ornek: Ctrl+Alt+A";
        }

        _messageWindow = new NativeMessageWindow();
        _messageWindow.HotKeyPressed += OnHotKeyPressed;
        _registered = _messageWindow.RegisterHotKey(HotKeyId, modifiers, virtualKey);
        return _registered
            ? null
            : "Kisayol kaydedilemedi (baska bir uygulama ayni tuslari kullaniyor olabilir).";
    }

    public void Stop()
    {
        if (_messageWindow is null)
        {
            return;
        }

        if (_registered)
        {
            _messageWindow.UnregisterHotKey(HotKeyId);
            _registered = false;
        }

        _messageWindow.HotKeyPressed -= OnHotKeyPressed;
        _messageWindow.Dispose();
        _messageWindow = null;
    }

    public void Dispose() => Stop();

    private void OnHotKeyPressed(object? sender, int id)
    {
        if (id == HotKeyId)
        {
            HotKeyPressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool TryParseHotKey(string hotKey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(hotKey))
        {
            return false;
        }

        var parts = hotKey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => NativeMethods.ModControl,
                "alt" => NativeMethods.ModAlt,
                "shift" => NativeMethods.ModShift,
                "win" or "windows" => NativeMethods.ModWin,
                _ => 0
            };
        }

        var key = parts[^1];
        virtualKey = key.Length == 1
            ? (uint)char.ToUpperInvariant(key[0])
            : key.ToUpperInvariant() switch
            {
                "SPACE" => 0x20,
                "0" => 0x30,
                "1" => 0x31,
                "2" => 0x32,
                "3" => 0x33,
                "4" => 0x34,
                "5" => 0x35,
                "6" => 0x36,
                "7" => 0x37,
                "8" => 0x38,
                "9" => 0x39,
                "F1" => 0x70,
                "F2" => 0x71,
                "F3" => 0x72,
                "F4" => 0x73,
                "F5" => 0x74,
                "F6" => 0x75,
                "F7" => 0x76,
                "F8" => 0x77,
                "F9" => 0x78,
                "F10" => 0x79,
                "F11" => 0x7A,
                "F12" => 0x7B,
                "A" => 0x41,
                "B" => 0x42,
                "C" => 0x43,
                "D" => 0x44,
                "E" => 0x45,
                "F" => 0x46,
                "G" => 0x47,
                "H" => 0x48,
                "I" => 0x49,
                "J" => 0x4A,
                "K" => 0x4B,
                "L" => 0x4C,
                "M" => 0x4D,
                "N" => 0x4E,
                "O" => 0x4F,
                "P" => 0x50,
                "Q" => 0x51,
                "R" => 0x52,
                "S" => 0x53,
                "T" => 0x54,
                "U" => 0x55,
                "V" => 0x56,
                "W" => 0x57,
                "X" => 0x58,
                "Y" => 0x59,
                "Z" => 0x5A,
                _ => 0
            };

        return virtualKey != 0 && modifiers != 0;
    }
}
