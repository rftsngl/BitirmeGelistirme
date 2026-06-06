using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WindowsAiAssistant.App.Overlay;

namespace WindowsAiAssistant.App.Overlay;

public sealed partial class AssistantOverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly OverlaySessionRunner _sessionRunner;

    public AssistantOverlayWindow(OverlayViewModel viewModel, OverlaySessionRunner sessionRunner)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _sessionRunner = sessionRunner ?? throw new ArgumentNullException(nameof(sessionRunner));

        InitializeComponent();
        Root.DataContext = _viewModel;
        ConfigureChrome();
        Activated += OnActivated;
        Closed += (_, _) => _sessionRunner.CancelActiveSession();
    }

    public void ShowAndPosition()
    {
        PositionBottomCenter();
        Activate();
        PlayShowAnimation();
    }

    private void PlayShowAnimation()
    {
        RootTranslate.Y = 28;
        RootBorder.Opacity = 0;

        var storyboard = new Storyboard();

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(180))
        };
        Storyboard.SetTarget(fade, RootBorder);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = 28,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, RootTranslate);
        Storyboard.SetTargetProperty(slide, "Y");

        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            return;
        }

        // Dis tikla kapatma yalnizca sonuc/hata gibi terminal fazlarda guvenli;
        // agent calisirken baska pencerelere odak verdiginde overlay kapanmamali.
        if (_viewModel.CanDismissOnFocusLoss)
        {
            HideOverlay();
        }
    }

    public void HideOverlay()
    {
        _sessionRunner.CancelActiveSession();
        AppWindow.Hide();
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
