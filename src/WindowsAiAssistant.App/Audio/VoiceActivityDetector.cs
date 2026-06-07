namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// PCM ses seviyesine gore konusma baslangici ve susma sonu algilar.
/// </summary>
internal sealed class VoiceActivityDetector
{
    private readonly int _silenceEndMs;
    private readonly int _minSpeechMs;
    private readonly double _speechMultiplier;
    private readonly int _calibrationMs;
    private readonly int _speechOnsetFrames;
    private double _noiseFloor;
    private int _calibratedSamples;
    private bool _speechStarted;
    private int _consecutiveSpeechFrames;
    private DateTimeOffset _firstSpeechUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSpeechUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;

    public VoiceActivityDetector(
        int silenceEndMs,
        double speechMultiplier = 3.8,
        int calibrationMs = 500,
        int minSpeechMs = 550,
        int speechOnsetFrames = 4)
    {
        _silenceEndMs = Math.Clamp(silenceEndMs, 400, 3000);
        _minSpeechMs = Math.Clamp(minSpeechMs, 200, 2000);
        _speechMultiplier = Math.Clamp(speechMultiplier, 1.5, 8.0);
        _calibrationMs = Math.Clamp(calibrationMs, 100, 1500);
        _speechOnsetFrames = Math.Clamp(speechOnsetFrames, 2, 12);
        _noiseFloor = 0.006;
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

        var threshold = Math.Max(_noiseFloor * _speechMultiplier, 0.012);
        var isSpeech = level >= threshold;
        if (isSpeech)
        {
            _consecutiveSpeechFrames++;
            if (!_speechStarted && _consecutiveSpeechFrames >= _speechOnsetFrames)
            {
                _speechStarted = true;
                _firstSpeechUtc = DateTimeOffset.UtcNow;
            }

            if (_speechStarted)
            {
                _lastSpeechUtc = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            _consecutiveSpeechFrames = 0;
        }

        return (level, _speechStarted && isSpeech);
    }

    public bool ShouldEndAfterSpeech()
    {
        if (!_speechStarted)
        {
            return false;
        }

        var speechDurationMs = (_lastSpeechUtc - _firstSpeechUtc).TotalMilliseconds;
        if (speechDurationMs < _minSpeechMs)
        {
            return false;
        }

        return (DateTimeOffset.UtcNow - _lastSpeechUtc).TotalMilliseconds >= _silenceEndMs;
    }

    public bool ExceededMaxWait(int maxWaitSeconds)
    {
        if (_speechStarted)
        {
            return (DateTimeOffset.UtcNow - _startedUtc).TotalSeconds >= Math.Max(maxWaitSeconds, 45);
        }

        return (DateTimeOffset.UtcNow - _startedUtc).TotalSeconds >= maxWaitSeconds;
    }

    public void Reset()
    {
        _speechStarted = false;
        _consecutiveSpeechFrames = 0;
        _firstSpeechUtc = DateTimeOffset.MinValue;
        _lastSpeechUtc = DateTimeOffset.MinValue;
        _startedUtc = DateTimeOffset.UtcNow;
        _calibratedSamples = 0;
        _noiseFloor = 0.006;
    }

    public static double MeasureLevel(ReadOnlySpan<byte> pcm16LeMono) =>
        ComputeNormalizedRms(pcm16LeMono);

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
