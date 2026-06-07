using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

internal static class SpeechEngineResolver
{
    public static ISpeechToTextService Create(
        AudioOptions options,
        SpeechReadinessService readiness,
        VoskWakeWordModelService voskModels,
        WhisperModelService whisperModels,
        MicrophoneSessionCoordinator microphone)
    {
        if (readiness.UsesWhisperForStt())
        {
            return new WhisperSpeechToTextService(options, readiness, whisperModels, microphone);
        }

        if (readiness.UsesVoskForStt())
        {
            return new VoskSpeechToTextService(options, readiness, voskModels);
        }

        return new WindowsSpeechToTextService(options, readiness);
    }
}
