using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
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
    private Visual? _rootVisual;
    private ScalarKeyFrameAnimation? _pulseAnimation;
    private bool _isPulsing;

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
    }

    public void ShowAndPosition()
    {
        PositionBottomCenter();
        Activate();
        EnsureCompositor();
        PlayShowAnimation();
    }

    private void EnsureCompositor()
    {
        if (_compositor is not null) return;
        _rootVisual = ElementCompositionPreview.GetElementVisual(RootBorder);
        _compositor = _rootVisual.Compositor;
    }

    private void PlayShowAnimation()
    {
        EnsureCompositor();
        if (_compositor is null || _rootVisual is null) return;

        _rootVisual.Opacity = 0f;
        _rootVisual.Scale = new Vector3(0.96f, 0.96f, 1f);
        _rootVisual.CenterPoint = new Vector3((float)(RootBorder.ActualWidth / 2), (float)RootBorder.ActualHeight, 0f);

        var fadeIn = _compositor.CreateScalarKeyFrameAnimation();
        fadeIn.InsertKeyFrame(0f, 0f);
        fadeIn.InsertKeyFrame(1f, 1f, _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        fadeIn.Duration = TimeSpan.FromMilliseconds(220);

        var scaleUp = _compositor.CreateVector3KeyFrameAnimation();
        scaleUp.InsertKeyFrame(0f, new Vector3(0.96f, 0.96f, 1f));
        scaleUp.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        scaleUp.Duration = TimeSpan.FromMilliseconds(280);

        _rootVisual.StartAnimation("Opacity", fadeIn);
        _rootVisual.StartAnimation("Scale", scaleUp);
    }

    private async void PlayHideAnimation()
    {
        EnsureCompositor();
        if (_compositor is null || _rootVisual is null)
        {
            AppWindow.Hide();
            return;
        }

        StopPulseAnimation();

        var fadeOut = _compositor.CreateScalarKeyFrameAnimation();
        fadeOut.InsertKeyFrame(0f, 1f);
        fadeOut.InsertKeyFrame(1f, 0f, _compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.4f, 0f), new Vector2(1f, 1f)));
        fadeOut.Duration = TimeSpan.FromMilliseconds(160);

        var scaleDown = _compositor.CreateVector3KeyFrameAnimation();
        scaleDown.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        scaleDown.InsertKeyFrame(1f, new Vector3(0.97f, 0.97f, 1f));
        scaleDown.Duration = TimeSpan.FromMilliseconds(160);

        _rootVisual.StartAnimation("Opacity", fadeOut);
        _rootVisual.StartAnimation("Scale", scaleDown);

        await Task.Delay(170);
        AppWindow.Hide();
    }

    private void StartPulseAnimation()
    {
        EnsureCompositor();
        if (_compositor is null || _rootVisual is null || _isPulsing) return;

        _pulseAnimation = _compositor.CreateScalarKeyFrameAnimation();
        _pulseAnimation.InsertKeyFrame(0f, 1f);
        _pulseAnimation.InsertKeyFrame(0.5f, 0.6f);
        _pulseAnimation.InsertKeyFrame(1f, 1f);
        _pulseAnimation.Duration = TimeSpan.FromMilliseconds(1800);
        _pulseAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

        var statusVisual = ElementCompositionPreview.GetElementVisual(StatusIcon);
        statusVisual.StartAnimation("Opacity", _pulseAnimation);
        _isPulsing = true;
    }

    private void StopPulseAnimation()
    {
        if (!_isPulsing) return;
        var statusVisual = ElementCompositionPreview.GetElementVisual(StatusIcon);
        statusVisual.StopAnimation("Opacity");
        statusVisual.Opacity = 1f;
        _isPulsing = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OverlayViewModel.Phase)) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            UpdatePhaseVisuals();
            switch (_viewModel.Phase)
            {
                case OverlayPhase.Listening:
                case OverlayPhase.Transcribing:
                    StartPulseAnimation();
                    break;
                default:
                    StopPulseAnimation();
                    break;
            }
        });
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

        // Dis tikla kapatma yalnizca bekleme/manuel giris/terminal fazlarda guvenli;
        // agent calisirken baska pencerelere odak verdiginde overlay kapanmamali.
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

    private void PositionBottomCenter()
    {
        const int width = 560;
        const int height = 240;

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
            workArea.Y + workArea.Height - height - 56));
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

    // P/Invoke for cursor-based monitor detection
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
