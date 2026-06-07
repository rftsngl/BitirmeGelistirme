using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WindowsAiAssistant.App.Overlay;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.Overlay;

public sealed partial class AssistantOverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly OverlaySessionRunner _sessionRunner;
    private Compositor? _compositor;
    private Visual? _cardVisual;
    private Visual? _orbVisual;
    private readonly List<(Ellipse Dot, int PhaseOffset)> _listenDots = [];
    private readonly List<(Ellipse Dot, int PhaseOffset)> _thinkDots = [];
    private bool _listenDotsAnimating;
    private bool _thinkDotsAnimating;

    public AssistantOverlayWindow(OverlayViewModel viewModel, OverlaySessionRunner sessionRunner)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _sessionRunner = sessionRunner ?? throw new ArgumentNullException(nameof(sessionRunner));

        InitializeComponent();
        Root.DataContext = _viewModel;
        ConfigureChrome();
        Activated += OnActivated;
        Closed += (_, _) => _sessionRunner.CancelActiveSession();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _listenDots.Add((ListenDot0, 0));
        _listenDots.Add((ListenDot1, 1));
        _listenDots.Add((ListenDot2, 2));
        _listenDots.Add((ListenDot3, 3));
        _thinkDots.Add((ThinkDot0, 0));
        _thinkDots.Add((ThinkDot1, 1));
        _thinkDots.Add((ThinkDot2, 2));
    }

    public void ShowAndPosition()
    {
        PositionBottomCenter(_viewModel.IsCompactMode);
        UpdateShellShape(_viewModel.IsCompactMode);
        AppWindow.Show();
        ConfigureTransparentSurface();
        ConfigureNonActivatingOverlay();
        SafeUi(() =>
        {
            EnsureCompositor();
            PlayShowAnimation();
            UpdateActivityAnimations();
        });
    }

    private void SafeUi(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "AssistantOverlayWindow UI");
        }
    }

    private void EnsureCompositor()
    {
        if (_compositor is not null)
        {
            return;
        }

        _cardVisual = ElementCompositionPreview.GetElementVisual(CardBorder);
        _orbVisual = ElementCompositionPreview.GetElementVisual(AssistantOrb);
        _compositor = _cardVisual.Compositor;
    }

    private void PlayShowAnimation()
    {
        EnsureCompositor();
        if (_compositor is null || _cardVisual is null)
        {
            return;
        }

        var ease = _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.05f, 0.7f),
            new Vector2(0.1f, 1f));

        _cardVisual.Opacity = 0f;
        _cardVisual.Offset = new Vector3(0f, 24f, 0f);
        _cardVisual.Scale = new Vector3(0.94f, 0.94f, 1f);

        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(280);

        var slide = _compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(0f, 24f, 0f));
        slide.InsertKeyFrame(1f, new Vector3(0f, 0f, 0f), ease);
        slide.Duration = TimeSpan.FromMilliseconds(320);

        var scale = _compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(0.94f, 0.94f, 1f));
        scale.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), ease);
        scale.Duration = TimeSpan.FromMilliseconds(320);

        _cardVisual.StartAnimation("Opacity", fade);
        _cardVisual.StartAnimation("Offset", slide);
        _cardVisual.StartAnimation("Scale", scale);
    }

    private async void PlayHideAnimation()
    {
        EnsureCompositor();
        if (_compositor is null || _cardVisual is null)
        {
            AppWindow.Hide();
            return;
        }

        StopAllActivityAnimations();

        var ease = _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.3f, 0f),
            new Vector2(1f, 1f));

        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 1f);
        fade.InsertKeyFrame(1f, 0f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(180);

        var slide = _compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(0f, 0f, 0f));
        slide.InsertKeyFrame(1f, new Vector3(0f, 14f, 0f), ease);
        slide.Duration = TimeSpan.FromMilliseconds(180);

        _cardVisual.StartAnimation("Opacity", fade);
        _cardVisual.StartAnimation("Offset", slide);

        await Task.Delay(190);
        AppWindow.Hide();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayViewModel.Phase) or nameof(OverlayViewModel.IsCompactMode))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                PositionBottomCenter(_viewModel.IsCompactMode);
                UpdateShellShape(_viewModel.IsCompactMode);
                UpdateActivityAnimations();
            });
        }

        if (e.PropertyName is nameof(OverlayViewModel.ShowListeningDots) or nameof(OverlayViewModel.ShowThinkingDots))
        {
            DispatcherQueue.TryEnqueue(UpdateActivityAnimations);
        }

        if (e.PropertyName is nameof(OverlayViewModel.IsSpeaking)
            or nameof(OverlayViewModel.IsAssistantSpeaking)
            or nameof(OverlayViewModel.ShowSpeakingActivity))
        {
            DispatcherQueue.TryEnqueue(UpdateSpeakingRing);
        }
    }

    private void UpdateShellShape(bool compact)
    {
        CardBorder.CornerRadius = compact
            ? new CornerRadius(28)
            : new CornerRadius(24);
    }

    private void UpdateActivityAnimations()
    {
        SafeUi(UpdateActivityAnimationsCore);
    }

    private void UpdateActivityAnimationsCore()
    {
        if (_viewModel.ShowListeningDots)
        {
            StopThinkingDotsAnimation();
            StartListeningDotsAnimation();
            StartOrbIdlePulse();
        }
        else if (_viewModel.ShowThinkingDots)
        {
            StopListeningDotsAnimation();
            StopOrbIdlePulse();
            StartThinkingDotsAnimation();
        }
        else if (_viewModel.ShowSpeakingActivity)
        {
            StopListeningDotsAnimation();
            StopThinkingDotsAnimation();
            StopOrbIdlePulse();
        }
        else
        {
            StopAllActivityAnimations();
        }
    }

    private void StartListeningDotsAnimation()
    {
        EnsureCompositor();
        if (_compositor is null || _listenDotsAnimating)
        {
            return;
        }

        foreach (var (dot, phaseOffset) in _listenDots)
        {
            var visual = ElementCompositionPreview.GetElementVisual(dot);
            visual.CenterPoint = new Vector3(4.5f, 4.5f, 0f);

            var anim = _compositor.CreateVector3KeyFrameAnimation();
            anim.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
            anim.InsertKeyFrame(0.35f, new Vector3(1f, 1.55f, 1f));
            anim.InsertKeyFrame(0.7f, new Vector3(1f, 0.85f, 1f));
            anim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
            anim.Duration = TimeSpan.FromMilliseconds(720);
            anim.IterationBehavior = AnimationIterationBehavior.Forever;
            anim.DelayBehavior = AnimationDelayBehavior.SetInitialValueAfterDelay;
            anim.DelayTime = TimeSpan.FromMilliseconds(phaseOffset * 110);

            visual.StartAnimation("Scale", anim);
        }

        _listenDotsAnimating = true;
    }

    private void StopListeningDotsAnimation()
    {
        if (!_listenDotsAnimating || _compositor is null)
        {
            return;
        }

        foreach (var (dot, _) in _listenDots)
        {
            var visual = ElementCompositionPreview.GetElementVisual(dot);
            visual.StopAnimation("Scale");
            visual.Scale = new Vector3(1f, 1f, 1f);
        }

        _listenDotsAnimating = false;
    }

    private void StartThinkingDotsAnimation()
    {
        EnsureCompositor();
        if (_compositor is null || _thinkDotsAnimating)
        {
            return;
        }

        foreach (var (dot, phaseOffset) in _thinkDots)
        {
            var visual = ElementCompositionPreview.GetElementVisual(dot);

            var opacity = _compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0f, 0.25f);
            opacity.InsertKeyFrame(0.45f, 1f);
            opacity.InsertKeyFrame(1f, 0.25f);
            opacity.Duration = TimeSpan.FromMilliseconds(900);
            opacity.IterationBehavior = AnimationIterationBehavior.Forever;
            opacity.DelayBehavior = AnimationDelayBehavior.SetInitialValueAfterDelay;
            opacity.DelayTime = TimeSpan.FromMilliseconds(phaseOffset * 180);

            visual.StartAnimation("Opacity", opacity);
        }

        _thinkDotsAnimating = true;
    }

    private void StopThinkingDotsAnimation()
    {
        if (!_thinkDotsAnimating || _compositor is null)
        {
            return;
        }

        foreach (var (dot, _) in _thinkDots)
        {
            var visual = ElementCompositionPreview.GetElementVisual(dot);
            visual.StopAnimation("Opacity");
            visual.Opacity = 0.5f;
        }

        _thinkDotsAnimating = false;
    }

    private void StartOrbIdlePulse()
    {
        EnsureCompositor();
        if (_compositor is null || _orbVisual is null || _viewModel.ShowSpeakingActivity)
        {
            return;
        }

        _orbVisual.StopAnimation("Scale");

        var pulse = _compositor.CreateVector3KeyFrameAnimation();
        pulse.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        pulse.InsertKeyFrame(0.5f, new Vector3(1.06f, 1.06f, 1f));
        pulse.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
        pulse.Duration = TimeSpan.FromMilliseconds(1400);
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;

        _orbVisual.CenterPoint = new Vector3(20f, 20f, 0f);
        _orbVisual.StartAnimation("Scale", pulse);
    }

    private void StopOrbIdlePulse()
    {
        if (_orbVisual is null)
        {
            return;
        }

        _orbVisual.StopAnimation("Scale");
        _orbVisual.Scale = new Vector3(1f, 1f, 1f);
    }

    private void StopAllActivityAnimations()
    {
        StopListeningDotsAnimation();
        StopThinkingDotsAnimation();
        StopOrbIdlePulse();
    }

    private void UpdateSpeakingRing()
    {
        var active = _viewModel.ShowSpeakingActivity;
        SpeakingRing.Opacity = active ? 1 : 0;
        if (active)
        {
            SpeakingRingScale.ScaleX = 1.2;
            SpeakingRingScale.ScaleY = 1.2;
            EnsureCompositor();
            if (_compositor is not null && _orbVisual is not null)
            {
                var pulse = _compositor.CreateVector3KeyFrameAnimation();
                pulse.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
                pulse.InsertKeyFrame(0.5f, new Vector3(1.08f, 1.08f, 1f));
                pulse.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
                pulse.Duration = TimeSpan.FromMilliseconds(700);
                pulse.IterationBehavior = AnimationIterationBehavior.Forever;
                _orbVisual.CenterPoint = new Vector3(20f, 20f, 0f);
                _orbVisual.StartAnimation("Scale", pulse);
            }
        }
        else
        {
            SpeakingRingScale.ScaleX = 1;
            SpeakingRingScale.ScaleY = 1;
            if (_viewModel.ShowListeningDots)
            {
                StartOrbIdlePulse();
            }
            else
            {
                StopOrbIdlePulse();
            }
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            return;
        }

        if (_viewModel.CanDismissOnFocusLoss)
        {
            HideOverlay();
        }
    }

    public void HideOverlay()
    {
        _sessionRunner.CancelActiveSession();
        StopAllActivityAnimations();
        PlayHideAnimation();
    }

    public Task RunVoiceSessionAsync(CancellationToken cancellationToken = default) =>
        _sessionRunner.RunAsync(this, cancellationToken);

    private void ConfigureChrome()
    {
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }
    }

    private void ConfigureNonActivatingOverlay()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            const int gwlExstyle = -20;
            const int wsExNoActivate = 0x08000000;
            const int wsExToolWindow = 0x00000080;
            var style = GetWindowLong(hwnd, gwlExstyle);
            SetWindowLong(hwnd, gwlExstyle, style | wsExNoActivate | wsExToolWindow);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "ConfigureNonActivatingOverlay");
        }
    }

    private void ConfigureTransparentSurface()
    {
        try
        {
            if (AppWindow.TitleBar is { } titleBar)
            {
                titleBar.ExtendsContentIntoTitleBar = true;
                var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.BackgroundColor = transparent;
                titleBar.InactiveBackgroundColor = transparent;
                titleBar.ButtonBackgroundColor = transparent;
                titleBar.ButtonInactiveBackgroundColor = transparent;
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            const int dwmwaSystemBackdropType = 38;
            const int dwmsbtTransientWindow = 3;
            var backdrop = dwmsbtTransientWindow;
            _ = DwmSetWindowAttribute(hwnd, dwmwaSystemBackdropType, ref backdrop, sizeof(int));
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "ConfigureTransparentSurface");
        }
    }

    private void PositionBottomCenter(bool compact)
    {
        const int compactWidth = 520;
        const int compactHeight = 76;
        const int expandedWidth = 540;
        const int expandedHeight = 320;

        var width = compact ? compactWidth : expandedWidth;
        var height = compact ? compactHeight : expandedHeight;

        RectInt32 workArea;
        if (TryGetCursorMonitorWorkArea(out var cursorWorkArea))
        {
            workArea = cursorWorkArea;
        }
        else
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            workArea = display.WorkArea;
        }

        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + workArea.Height - height - 48));
    }

    private static bool TryGetCursorMonitorWorkArea(out RectInt32 workArea)
    {
        workArea = default;
        if (!GetCursorPos(out var cursorPoint))
        {
            return false;
        }

        var hMonitor = MonitorFromPoint(cursorPoint, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref info))
        {
            return false;
        }

        workArea = new RectInt32(
            info.rcWork.left,
            info.rcWork.top,
            info.rcWork.right - info.rcWork.left,
            info.rcWork.bottom - info.rcWork.top);
        return true;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideOverlay();
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => HideOverlay();

    private void ApproveButton_Click(object sender, RoutedEventArgs e) =>
        _sessionRunner.ApproveOverlayPending();

    private void DenyButton_Click(object sender, RoutedEventArgs e) =>
        _sessionRunner.DenyOverlayPending();

    private void ManualSubmitButton_Click(object sender, RoutedEventArgs e) =>
        _sessionRunner.SubmitManualInput(_viewModel.ManualInputText);

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static int GetWindowLong(IntPtr hwnd, int index) =>
        IntPtr.Size == 8
            ? (int)GetWindowLongPtr64(hwnd, index)
            : GetWindowLong32(hwnd, index);

    private static void SetWindowLong(IntPtr hwnd, int index, int value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hwnd, index, new IntPtr(value));
        }
        else
        {
            SetWindowLong32(hwnd, index, value);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
