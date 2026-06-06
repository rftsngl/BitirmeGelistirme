using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.Runtime.Config;
using WindowsAiAssistant.Runtime.Policy;

namespace WindowsAiAssistant.App.Services;

public sealed class LocalAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _localPath;
    private readonly AudioOptions _audio;
    private readonly RuntimeOptions _runtime;

    public LocalAppSettingsService(AudioOptions audio, RuntimeOptions runtime)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _localPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");
    }

    public void SaveAudioAndPolicy(
        AudioOptions audioSnapshot,
        ActionPolicy policySnapshot,
        UiAutomationOptions uiAutomationSnapshot,
        bool startWithWindows)
    {
        ProtectPorcupineKeyForStorage(audioSnapshot);

        CopyAudio(audioSnapshot, _audio);
        CopyPolicy(policySnapshot, _runtime.ActionPolicy);
        CopyUiAutomation(uiAutomationSnapshot, _runtime.UiAutomation);
        _audio.StartWithWindows = startWithWindows;

        var root = LoadRootObject();
        root["Audio"] = JsonSerializer.SerializeToNode(audioSnapshot, JsonOptions);
        root["Runtime"] ??= new JsonObject();
        if (root["Runtime"] is JsonObject runtimeNode)
        {
            runtimeNode["ActionPolicy"] = JsonSerializer.SerializeToNode(policySnapshot, JsonOptions);
            runtimeNode["UiAutomation"] = JsonSerializer.SerializeToNode(uiAutomationSnapshot, JsonOptions);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_localPath)!);
        File.WriteAllText(_localPath, root.ToJsonString(JsonOptions));
        WindowsStartupService.SetEnabled(startWithWindows);
    }

    /// <summary>
    /// Bellekte duz metin tutulabilir; diske yazarken DPAPI ile korur.
    /// </summary>
    public static void ProtectPorcupineKeyForStorage(AudioOptions audio)
    {
        if (string.IsNullOrWhiteSpace(audio.PorcupineAccessKey) ||
            audio.PorcupineAccessKey.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            return;
        }

        audio.PorcupineAccessKey = SecretProtector.Protect(audio.PorcupineAccessKey.Trim());
    }

    public static void CopyAudio(AudioOptions from, AudioOptions to)
    {
        to.BackgroundModeEnabled = from.BackgroundModeEnabled;
        to.StartMinimizedToTray = from.StartMinimizedToTray;
        to.WakeWordEnabled = from.WakeWordEnabled;
        to.PorcupineAccessKey = from.PorcupineAccessKey;
        to.PorcupineKeywordPath = from.PorcupineKeywordPath;
        to.PorcupineModelPath = from.PorcupineModelPath;
        to.PorcupineSensitivity = from.PorcupineSensitivity;
        to.GlobalHotKeyEnabled = from.GlobalHotKeyEnabled;
        to.GlobalHotKey = from.GlobalHotKey;
        to.TextToSpeechEnabled = from.TextToSpeechEnabled;
        to.SpeechLanguage = from.SpeechLanguage;
        to.SpeechListenTimeoutSeconds = from.SpeechListenTimeoutSeconds;
        to.OverlayAutoCloseSeconds = from.OverlayAutoCloseSeconds;
        to.StartWithWindows = from.StartWithWindows;
        to.VoiceApprovalEnabled = from.VoiceApprovalEnabled;
        to.SpeechEngine = from.SpeechEngine;
        to.WhisperModelPath = from.WhisperModelPath;
        to.InputDeviceIndex = from.InputDeviceIndex;
        to.DeveloperModeEnabled = from.DeveloperModeEnabled;
    }

    public static void CopyPolicy(ActionPolicy from, ActionPolicy to)
    {
        to.Normal = from.Normal;
        to.Sensitive = from.Sensitive;
        to.Destructive = from.Destructive;
        to.AllowSessionRemember = from.AllowSessionRemember;
    }

    public static void CopyUiAutomation(UiAutomationOptions from, UiAutomationOptions to)
    {
        to.MaxDepth = from.MaxDepth;
        to.MaxElementsPerStep = from.MaxElementsPerStep;
        to.CaptureTimeoutMs = from.CaptureTimeoutMs;
        to.EnableChromiumAccessibility = from.EnableChromiumAccessibility;
        to.EnableUia2Fallback = from.EnableUia2Fallback;
        to.Uia2FallbackMinElements = from.Uia2FallbackMinElements;
    }

    private JsonObject LoadRootObject()
    {
        if (!File.Exists(_localPath))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(_localPath)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }
}
