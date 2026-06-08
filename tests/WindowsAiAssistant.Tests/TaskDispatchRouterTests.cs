using WindowsAiAssistant.Agent.Dispatch;

namespace WindowsAiAssistant.Tests;

public sealed class TaskDispatchRouterTests
{
    private readonly TaskDispatchRouter _router = new();

    [Fact]
    public void TryResolve_MuteGoal_ReturnsAudioPower()
    {
        var decision = _router.TryResolve("sesi kapat", observation: null);

        Assert.NotNull(decision);
        Assert.Equal("audio_power", decision!.Action);
        Assert.Equal("mute", decision.Parameters["mode"]);
    }

    [Fact]
    public void TryResolve_NetworkStatusGoal_ReturnsNetworkStatus()
    {
        var decision = _router.TryResolve("internet baglantisi var mi", observation: null);

        Assert.NotNull(decision);
        Assert.Equal("network_status", decision!.Action);
    }

    [Fact]
    public void TryResolve_NewDocumentGoal_ReturnsCtrlNShortcut()
    {
        var decision = _router.TryResolve("yeni belge olustur", observation: null);

        Assert.NotNull(decision);
        Assert.Equal("press_shortcut", decision!.Action);
        Assert.Equal("Ctrl+N", decision.Target);
    }

    [Fact]
    public void TryResolve_SelectAllGoal_ReturnsCtrlAShortcut()
    {
        var decision = _router.TryResolve("tumunu sec", observation: null);

        Assert.NotNull(decision);
        Assert.Equal("press_shortcut", decision!.Action);
        Assert.Equal("Ctrl+A", decision.Target);
    }

    [Fact]
    public void TryResolve_SaveDocumentGoal_ReturnsCtrlSShortcut()
    {
        var decision = _router.TryResolve("belgeyi kaydet", observation: null);

        Assert.NotNull(decision);
        Assert.Equal("press_shortcut", decision!.Action);
        Assert.Equal("Ctrl+S", decision.Target);
    }

    [Fact]
    public void TryResolve_ClipboardGoal_ReturnsClipboardRead()
    {
        var decision = _router.TryResolve("panoda ne var", observation: null);

        Assert.NotNull(decision);
        Assert.Equal("clipboard", decision!.Action);
        Assert.Equal("read", decision.Parameters["mode"]);
    }

    [Fact]
    public void ShouldCompleteAfterRoute_MatchingSaveRoute_ReturnsTrue()
    {
        Assert.True(_router.ShouldCompleteAfterRoute("dosyayi kaydet", "press_shortcut"));
    }

    [Fact]
    public void TryResolve_CompositeGoal_ReturnsNull()
    {
        var decision = _router.TryResolve("yeni belge olustur ve kaydet", observation: null);

        Assert.Null(decision);
    }

    [Fact]
    public void TryResolve_UnrelatedGoal_ReturnsNull()
    {
        var decision = _router.TryResolve("masaustunu temizle", observation: null);

        Assert.Null(decision);
    }

    [Fact]
    public void TryResolve_PerfGoal_ReturnsPerfCounter()
    {
        var decision = _router.TryResolve("cpu kullanimi nedir", observation: null);

        Assert.NotNull(decision);
        Assert.Equal("perf_counter", decision!.Action);
    }
}
