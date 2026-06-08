using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WindowsAiAssistant.Agent;
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
    private readonly AgentOptions _agent;
    private readonly AudioOptions _audio;
    private readonly RuntimeOptions _runtime;

    public LocalAppSettingsService(AgentOptions agent, AudioOptions audio, RuntimeOptions runtime)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _localPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");
    }

    public void SaveSettings(
        AgentOptions agentSnapshot,
        AudioOptions audioSnapshot,
        ActionPolicy policySnapshot,
        UiAutomationOptions uiAutomationSnapshot,
        bool startWithWindows)
    {
        CopyAgent(agentSnapshot, _agent);
        CopyAudio(audioSnapshot, _audio);
        CopyPolicy(policySnapshot, _runtime.ActionPolicy);
        CopyUiAutomation(uiAutomationSnapshot, _runtime.UiAutomation);
        _audio.StartWithWindows = startWithWindows;

        var root = LoadRootObject();
        if (root["Agent"] is not JsonObject agentNode)
        {
            agentNode = new JsonObject();
            root["Agent"] = agentNode;
        }

        agentNode["MaxSteps"] = agentSnapshot.MaxSteps;
        agentNode["MaxPriorStepsInPrompt"] = agentSnapshot.MaxPriorStepsInPrompt;

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

    public static void CopyAgent(AgentOptions from, AgentOptions to)
    {
        to.MaxSteps = from.MaxSteps;
        to.MaxPriorStepsInPrompt = from.MaxPriorStepsInPrompt;
        to.MaxParseRetries = from.MaxParseRetries;
        to.MaxSameActionFailures = from.MaxSameActionFailures;
        to.UserResponseLanguage = from.UserResponseLanguage;
    }

    public static void CopyAudio(AudioOptions from, AudioOptions to)
    {
        to.BackgroundModeEnabled = from.BackgroundModeEnabled;
        to.StartMinimizedToTray = from.StartMinimizedToTray;
        to.WakeWordEnabled = from.WakeWordEnabled;
        to.WakeWordEngine = from.WakeWordEngine;
        to.WakeWordPhrase = from.WakeWordPhrase;
        to.GlobalHotKeyEnabled = from.GlobalHotKeyEnabled;
        to.GlobalHotKey = from.GlobalHotKey;
        to.TextToSpeechEnabled = from.TextToSpeechEnabled;
        to.TtsEngine = from.TtsEngine;
        to.TtsVoiceName = from.TtsVoiceName;
        to.TtsSpeakingRate = from.TtsSpeakingRate;
        to.FollowUpListenTimeoutSeconds = from.FollowUpListenTimeoutSeconds;
        to.SpeechLanguage = from.SpeechLanguage;
        to.SpeechListenTimeoutSeconds = from.SpeechListenTimeoutSeconds;
        to.SilenceEndMilliseconds = from.SilenceEndMilliseconds;
        to.VadSpeechMultiplier = from.VadSpeechMultiplier;
        to.VadMinSpeechMilliseconds = from.VadMinSpeechMilliseconds;
        to.VadCalibrationMilliseconds = from.VadCalibrationMilliseconds;
        to.MicGainTargetPeak = from.MicGainTargetPeak;
        to.SttMinTranscriptCharacters = from.SttMinTranscriptCharacters;
        to.WakeWordFinalOnly = from.WakeWordFinalOnly;
        to.WakeWordCooldownSeconds = from.WakeWordCooldownSeconds;
        to.WakeWordRequireSpeechEnergy = from.WakeWordRequireSpeechEnergy;
        to.WakeWordMinPeakLevel = from.WakeWordMinPeakLevel;
        to.OverlayAutoCloseSeconds = from.OverlayAutoCloseSeconds;
        to.StartWithWindows = from.StartWithWindows;
        to.VoiceApprovalEnabled = from.VoiceApprovalEnabled;
        to.SpeechEngine = from.SpeechEngine;
        to.WhisperModelVariant = from.WhisperModelVariant;
        to.WhisperModelPath = from.WhisperModelPath;
        to.VoskModelVariant = from.VoskModelVariant;
        to.VoskModelPath = from.VoskModelPath;
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
