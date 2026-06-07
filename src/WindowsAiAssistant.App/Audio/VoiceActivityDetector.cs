namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// PCM ses seviyesine gore konusma baslangici ve susma sonu algilar.
/// </summary>
internal sealed class VoiceActivityDetector
{
    private readonly int _silenceEndMs;
    private readonly double _speechMultiplier;
    private readonly int _calibrationMs;
    private double _noiseFloor;
    private int _calibratedSamples;
    private bool _speechStarted;
    private DateTimeOffset _lastSpeechUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;

    public VoiceActivityDetector(int silenceEndMs, double speechMultiplier = 3.2, int calibrationMs = 350)
    {
        _silenceEndMs = Math.Clamp(silenceEndMs, 300, 3000);
        _speechMultiplier = Math.Clamp(speechMultiplier, 1.5, 8.0);
        _calibrationMs = Math.Clamp(calibrationMs, 100, 1000);
        _noiseFloor = 0.008;
    }

    public bool SpeechStarted => _speechStarted;

    public (double Level, bool IsSpeech) Process(ReadOnlySpan<byte> pcm16LeMono)
    {
        if (pcm16LeMono.Length < 2)
        {
            return (0, false);
        }

        var level = ComputeNormalizedRms(pcm16LeMono);
        UpdateNoiseFloor(level);

        var threshold = Math.Max(_noiseFloor * _speechMultiplier, 0.02);
        var isSpeech = level >= threshold;
        if (isSpeech)
        {
            _speechStarted = true;
            _lastSpeechUtc = DateTimeOffset.UtcNow;
        }

        return (level, isSpeech);
    }

    public bool ShouldEndAfterSpeech()
    {
        if (!_speechStarted)
        {
            return false;
        }

        return (DateTimeOffset.UtcNow - _lastSpeechUtc).TotalMilliseconds >= _silenceEndMs;
    }

    public bool ExceededMaxWait(int maxWaitSeconds)
    {
        if (_speechStarted)
        {
            return (DateTimeOffset.UtcNow - _startedUtc).TotalSeconds >= Math.Max(maxWaitSeconds, 30);
        }

        return (DateTimeOffset.UtcNow - _startedUtc).TotalSeconds >= maxWaitSeconds;
    }

    private void UpdateNoiseFloor(double level)
    {
        if (_speechStarted)
        {
            return;
        }

        var elapsedMs = (DateTimeOffset.UtcNow - _startedUtc).TotalMilliseconds;
        if (elapsedMs > _calibrationMs)
        {
            return;
        }

        _calibratedSamples++;
        _noiseFloor = ((_noiseFloor * (_calibratedSamples - 1)) + level) / _calibratedSamples;
    }

    private static double ComputeNormalizedRms(ReadOnlySpan<byte> pcm16LeMono)
    {
        var sampleCount = pcm16LeMono.Length / 2;
        if (sampleCount == 0)
        {
            return 0;
        }

        double sumSquares = 0;
        for (var i = 0; i < pcm16LeMono.Length - 1; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm16LeMono.Slice(i, 2));
            var normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        return Math.Sqrt(sumSquares / sampleCount);
    }
}
