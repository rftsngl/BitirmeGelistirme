using NAudio.Wave;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.Audio;

internal static class MicrophoneCapture
{
    public const int TargetSampleRate = 16000;
    private static readonly int[] PreferredSampleRates = [48000, 44100, 32000, 16000];

    public static int ResolveDeviceNumber(int requested)
    {
        if (requested < 0)
        {
            return -1;
        }

        return requested < WaveInEvent.DeviceCount ? requested : -1;
    }

    public static WaveInEvent OpenWaveIn(
        AudioOptions options,
        int bufferMilliseconds,
        out int captureSampleRate)
    {
        var deviceNumber = ResolveDeviceNumber(options.InputDeviceIndex);
        Exception? lastError = null;

        foreach (var sampleRate in PreferredSampleRates)
        {
            WaveInEvent? waveIn = null;
            try
            {
                waveIn = new WaveInEvent
                {
                    DeviceNumber = deviceNumber,
                    WaveFormat = new WaveFormat(sampleRate, 16, 1),
                    BufferMilliseconds = Math.Clamp(bufferMilliseconds, 20, 200)
                };
                waveIn.StartRecording();
                captureSampleRate = sampleRate;
                return waveIn;
            }
            catch (Exception ex)
            {
                lastError = ex;
                waveIn?.Dispose();
            }
        }

        var failure = lastError ?? new InvalidOperationException("Mikrofon acilamadi.");
        CrashLog.Write(failure, "MicrophoneCapture.OpenWaveIn");
        throw new SpeechAccessException(
            "Mikrofon acilamadi. Windows Ayarlar → Gizlilik → Mikrofon iznini ve secili cihazi kontrol edin.",
            failure);
    }

    public static byte[] ToTargetRate(ReadOnlySpan<byte> pcm16LeMono, int captureSampleRate, Pcm16Resampler? resampler)
    {
        if (captureSampleRate == TargetSampleRate)
        {
            return pcm16LeMono.ToArray();
        }

        resampler ??= new Pcm16Resampler(captureSampleRate, TargetSampleRate);
        return resampler.Process(pcm16LeMono);
    }

    public static byte[] ToTargetRate(byte[] pcm16LeMono, int captureSampleRate) =>
        captureSampleRate == TargetSampleRate
            ? pcm16LeMono
            : new Pcm16Resampler(captureSampleRate, TargetSampleRate).ResampleBuffer(pcm16LeMono);

    public static void ProcessChunk(
        ReadOnlySpan<byte> rawChunk,
        int captureSampleRate,
        Pcm16Resampler? resampler,
        AudioOptions options,
        Action<byte[], double> onReadyChunk)
    {
        var chunk = ToTargetRate(rawChunk, captureSampleRate, resampler);
        if (chunk.Length < 2)
        {
            return;
        }

        PcmAudioNormalizer.ApplyGain(chunk, options.MicGainTargetPeak);
        var level = VoiceActivityDetector.MeasureLevel(chunk);
        onReadyChunk(chunk, level);
    }
}
