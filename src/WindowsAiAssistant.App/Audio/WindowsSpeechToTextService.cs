using Windows.Media.SpeechRecognition;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

public sealed class WindowsSpeechToTextService : ISpeechToTextService
{
    private readonly AudioOptions _options;

    public WindowsSpeechToTextService(AudioOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<string?> ListenOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SpeechRecognizer? recognizer = null;
        try
        {
            recognizer = CreateRecognizer();
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                SpeechRecognitionScenario.Dictation,
                "dictation"));
            await recognizer.CompileConstraintsAsync().AsTask().ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.SpeechListenTimeoutSeconds, 3, 60)));

            var recognizeTask = recognizer.RecognizeAsync().AsTask();
            var completed = await Task.WhenAny(recognizeTask, Task.Delay(Timeout.Infinite, timeoutCts.Token))
                .ConfigureAwait(false);

            if (completed != recognizeTask)
            {
                return null;
            }

            var result = await recognizeTask.ConfigureAwait(false);
            return result.Status == SpeechRecognitionResultStatus.Success &&
                   !string.IsNullOrWhiteSpace(result.Text)
                ? result.Text.Trim()
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            recognizer?.Dispose();
        }
    }

    private SpeechRecognizer CreateRecognizer()
    {
        if (string.IsNullOrWhiteSpace(_options.SpeechLanguage))
        {
            return new SpeechRecognizer();
        }

        var language = new Windows.Globalization.Language(_options.SpeechLanguage);
        var supported = SpeechRecognizer.SupportedTopicLanguages
            .Any(item => item.LanguageTag.Equals(language.LanguageTag, StringComparison.OrdinalIgnoreCase));
        return supported ? new SpeechRecognizer(language) : new SpeechRecognizer();
    }
}
