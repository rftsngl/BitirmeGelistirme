using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Tests;

public sealed class ObservationIncrementalPolicyTests
{
    private const string Fingerprint = "Notepad|notepad|123|100|200|5";

    [Fact]
    public void ShouldReuse_StaticDesktopWithoutActionResult_ReturnsTrue()
    {
        Assert.True(ObservationIncrementalPolicy.ShouldReuse(
            enableIncremental: true,
            fullCaptureEveryNSteps: 3,
            stepIndex: 1,
            lastActionResult: null,
            currentFingerprint: Fingerprint,
            previousFingerprint: Fingerprint));
    }

    [Fact]
    public void ShouldReuse_AfterActionResult_ReturnsFalse()
    {
        Assert.False(ObservationIncrementalPolicy.ShouldReuse(
            true,
            3,
            1,
            "shell exitCode=0",
            Fingerprint,
            Fingerprint));
    }

    [Fact]
    public void ShouldReuse_FingerprintChanged_ReturnsFalse()
    {
        Assert.False(ObservationIncrementalPolicy.ShouldReuse(
            true,
            3,
            1,
            null,
            "Chrome|chrome|9|10|20|8",
            Fingerprint));
    }

    [Fact]
    public void ShouldReuse_FullCaptureStep_ReturnsFalse()
    {
        Assert.False(ObservationIncrementalPolicy.ShouldReuse(
            true,
            3,
            0,
            null,
            Fingerprint,
            Fingerprint));
    }
}
