namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Dusuk seviyeli mikrofon girisini hafifce yukseltir; clipping onler.
/// </summary>
internal static class PcmAudioNormalizer
{
    public static void ApplyGain(Span<byte> pcm16LeMono, double targetPeak = 0.55)
    {
        if (pcm16LeMono.Length < 2)
        {
            return;
        }

        var peak = 0;
        for (var i = 0; i < pcm16LeMono.Length - 1; i += 2)
        {
            var sample = Math.Abs(BitConverter.ToInt16(pcm16LeMono.Slice(i, 2)));
            if (sample > peak)
            {
                peak = sample;
            }
        }

        var currentPeak = peak / 32768.0;
        double gain;
        if (peak < 80)
        {
            // Cok dusuk sinyal: tamamen yok sayma — sabit kazanc uygula
            gain = 4.0;
        }
        else if (peak < 400)
        {
            gain = Math.Clamp(targetPeak / Math.Max(currentPeak, 0.004), 2.0, 4.0);
        }
        else
        {
            gain = Math.Clamp(targetPeak / currentPeak, 1.0, 2.8);
        }

        for (var i = 0; i < pcm16LeMono.Length - 1; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm16LeMono.Slice(i, 2));
            var amplified = (int)Math.Clamp(sample * gain, short.MinValue, short.MaxValue);
            var bytes = BitConverter.GetBytes((short)amplified);
            pcm16LeMono[i] = bytes[0];
            pcm16LeMono[i + 1] = bytes[1];
        }
    }
}
