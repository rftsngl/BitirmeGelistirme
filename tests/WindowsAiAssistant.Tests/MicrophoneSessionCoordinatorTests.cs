using WindowsAiAssistant.Runtime.Audio;

namespace WindowsAiAssistant.Tests;

public sealed class MicrophoneSessionCoordinatorTests
{
    [Fact]
    public async Task AcquireAsync_ConcurrentSecondCall_BlocksUntilRelease()
    {
        var coordinator = new MicrophoneSessionCoordinator();
        using var first = await coordinator.AcquireAsync("wake_word");

        var secondStarted = false;
        var secondAcquired = false;

        var secondTask = Task.Run(async () =>
        {
            secondStarted = true;
            using var second = await coordinator.AcquireAsync("stt");
            secondAcquired = true;
        });

        await Task.Delay(120);
        Assert.True(secondStarted);
        Assert.False(secondAcquired);
        Assert.True(coordinator.IsHeld);
        Assert.Equal("wake_word", coordinator.CurrentOwner);

        first.Dispose();
        await secondTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(secondAcquired);
        Assert.False(coordinator.IsHeld);
        Assert.Null(coordinator.CurrentOwner);
    }
}
