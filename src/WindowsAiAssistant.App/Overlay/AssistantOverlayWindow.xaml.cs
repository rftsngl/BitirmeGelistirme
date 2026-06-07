using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WindowsAiAssistant.App.Overlay;

namespace WindowsAiAssistant.App.Overlay;

public sealed partial class AssistantOverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly OverlaySessionRunner _sessionRunner;
    private Compositor? _compositor;
    private Visual? _cardVisual;
    private Visual? _statusIconVisual;
    private bool _isPulsing;

    public AssistantOverlayWindow(OverlayViewModel viewModel, OverlaySessionRunner sessionRunner)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _sessionRunner = sessionRunner ?? throw new ArgumentNullException(nameof(sessionRunner));

        InitializeComponent();
        Root.DataContext = _viewModel;
        ConfigureChrome();
        CardBorder.Loaded += OnLoaded;
        Activated += OnActivated;
        Closed += (_, _) => _sessionRunner.CancelActiveSession();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void ShowAndPosition()
    {
        PositionBottomCenter(_viewModel.IsCompactMode);
        AppWindow.Show();
        Activate();
        EnsureCompositor();
        PlayShowAnimation();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (CardBorder.Shadow is ThemeShadow themeShadow)
        {
            themeShadow.Receivers.Add(CardBorder);
        }
    }

    private void EnsureCompositor()
    {
        if (_compositor is not null)
        {
            return;
        }

        _cardVisual = ElementCompositionPreview.GetElementVisual(CardBorder);
        _statusIconVisual = ElementCompositionPreview.GetElementVisual(StatusIconHost);
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
        _cardVisual.Offset = new Vector3(0f, 28f, 0f);
        _cardVisual.Scale = new Vector3(0.96f, 0.96f, 1f);

        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(260);

        var slide = _compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(0f, 28f, 0f));
        slide.InsertKeyFrame(1f, new Vector3(0f, 0f, 0f), ease);
        slide.Duration = TimeSpan.FromMilliseconds(300);

        var scale = _compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(0.96f, 0.96f, 1f));
        scale.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), ease);
        scale.Duration = TimeSpan.FromMilliseconds(300);

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

        StopPulseAnimation();

        var ease = _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.3f, 0f),
            new Vector2(1f, 1f));

        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 1f);
        fade.InsertKeyFrame(1f, 0f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(160);

        var slide = _compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(0f, 0f, 0f));
        slide.InsertKeyFrame(1f, new Vector3(0f, 16f, 0f), ease);
        slide.Duration = TimeSpan.FromMilliseconds(160);

        _cardVisual.StartAnimation("Opacity", fade);
        _cardVisual.StartAnimation("Offset", slide);

        await Task.Delay(170);
        AppWindow.Hide();
    }

    private void StartPulseAnimation()
    {
        EnsureCompositor();
        if (_compositor is null || _statusIconVisual is null || _isPulsing)
        {
            return;
        }

        var scalePulse = _compositor.CreateVector3KeyFrameAnimation();
        scalePulse.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        scalePulse.InsertKeyFrame(0.5f, new Vector3(1.12f, 1.12f, 1f));
        scalePulse.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
        scalePulse.Duration = TimeSpan.FromMilliseconds(900);
        scalePulse.IterationBehavior = AnimationIterationBehavior.Forever;

        _statusIconVisual.CenterPoint = new Vector3(16f, 16f, 0f);
        _statusIconVisual.StartAnimation("Scale", scalePulse);
        _isPulsing = true;
    }

    private void StopPulseAnimation()
    {
        if (!_isPulsing || _statusIconVisual is null)
        {
            return;
        }

        _statusIconVisual.StopAnimation("Scale");
        _statusIconVisual.Scale = new Vector3(1f, 1f, 1f);
        _isPulsing = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayViewModel.Phase) or nameof(OverlayViewModel.IsCompactMode))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdatePhaseVisuals();
                PositionBottomCenter(_viewModel.IsCompactMode);

                if (_viewModel.Phase == OverlayPhase.Listening)
                {
                    StartPulseAnimation();
                }
                else
                {
                    StopPulseAnimation();
                }
            });
        }

        if (e.PropertyName is nameof(OverlayViewModel.IsSpeaking)
            or nameof(OverlayViewModel.IsAssistantSpeaking)
            or nameof(OverlayViewModel.ShowSpeakingActivity))
        {
            DispatcherQueue.TryEnqueue(UpdateSpeakingRing);
        }
    }

    private void UpdateSpeakingRing()
    {
        var active = _viewModel.ShowSpeakingActivity;
        SpeakingRing.Opacity = active ? 1 : 0;
        if (active)
        {
            SpeakingRingScale.ScaleX = 1.15;
            SpeakingRingScale.ScaleY = 1.15;
        }
        else
        {
            SpeakingRingScale.ScaleX = 1;
            SpeakingRingScale.ScaleY = 1;
        }
    }

    private void UpdatePhaseVisuals()
    {
        StatusIcon.Glyph = _viewModel.Phase switch
        {
            OverlayPhase.ManualInput => "\uE70F",
            OverlayPhase.ApprovalPending => "\uE7BA",
            OverlayPhase.Result => "\uE73E",
            OverlayPhase.Error => "\uE783",
            OverlayPhase.Running => "\uE768",
            _ => "\uE1D6"
        };
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
        StopPulseAnimation();
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

    private void PositionBottomCenter(bool compact)
    {
        const int compactWidth = 560;
        const int compactHeight = 72;
        const int expandedWidth = 580;
        const int expandedHeight = 280;

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
            workArea.Y + workArea.Height - height - 40));
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
