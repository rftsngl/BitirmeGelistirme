using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.Runtime.Audio;

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
            return new VoskSpeechToTextService(options, readiness, voskModels, microphone);
        }

        return new WindowsSpeechToTextService(options, readiness);
    }
}
