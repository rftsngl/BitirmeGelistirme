namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// PCM16 mono akisini hedef ornekleme hizina donusturur (or. 48kHz -> 16kHz).
/// </summary>
internal sealed class Pcm16Resampler
{
    private readonly double _step;
    private double _position;

    public Pcm16Resampler(int sourceSampleRate, int targetSampleRate = 16000)
    {
        if (sourceSampleRate <= 0 || targetSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSampleRate));
        }

        _step = (double)sourceSampleRate / targetSampleRate;
    }

    public byte[] Process(ReadOnlySpan<byte> pcm16LeMono)
    {
        var sampleCount = pcm16LeMono.Length / 2;
        if (sampleCount == 0)
        {
            return Array.Empty<byte>();
        }

        if (Math.Abs(_step - 1.0) < 0.001)
        {
            return pcm16LeMono.ToArray();
        }

        var output = new List<short>(sampleCount);
        var pos = _position;

        while (pos < sampleCount)
        {
            var index = (int)pos;
            var fraction = pos - index;
            short sample;
            if (index + 1 < sampleCount)
            {
                var s0 = BitConverter.ToInt16(pcm16LeMono.Slice(index * 2, 2));
                var s1 = BitConverter.ToInt16(pcm16LeMono.Slice((index + 1) * 2, 2));
                sample = (short)Math.Clamp(s0 + ((s1 - s0) * fraction), short.MinValue, short.MaxValue);
            }
            else if (index < sampleCount)
            {
                sample = BitConverter.ToInt16(pcm16LeMono.Slice(index * 2, 2));
            }
            else
            {
                break;
            }

            output.Add(sample);
            pos += _step;
        }

        _position = pos - sampleCount;

        var bytes = new byte[output.Count * 2];
        for (var i = 0; i < output.Count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2, 2), output[i]);
        }

        return bytes;
    }

    public byte[] ResampleBuffer(byte[] pcm16LeMono) =>
        Process(pcm16LeMono);
}
