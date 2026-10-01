using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using GamingLiveTranslator.Utilities;
using GamingLiveTranslator.ViewModels;

namespace GamingLiveTranslator.Views;

/// <summary>
/// Interaction logic for OverlayWindow.xaml.
/// Configures Win32 extended styles (WS_EX_NOACTIVATE, WS_EX_TRANSPARENT),
/// drag & resize handling, and coordinate persistence.
/// </summary>
public partial class OverlayWindow : Window
{
    private OverlayViewModel? _viewModel;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isApplyingBounds;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // CRITICAL: Extended window styles (WS_EX_NOACTIVATE, WS_EX_TRANSPARENT) require a valid HWND.
        // In WPF, the HWND is only created when SourceInitialized fires, not in the constructor.
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        LocationChanged += OnLocationOrSizeChanged;
        SizeChanged += (s, e) => OnLocationOrSizeChanged(s, EventArgs.Empty);

        if (_viewModel != null)
        {
            _viewModel.RequestOverlayVisibilityChanged += OnOverlayVisibilityChanged;
            _viewModel.RequestClickThroughChanged += OnClickThroughChanged;
            _viewModel.RequestResetPosition += OnResetPositionRequested;
        }

        // Drag handle
        DragBar.MouseDown += OnDragBarMouseDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd != IntPtr.Zero && _viewModel != null)
        {
            // Apply WS_EX_NOACTIVATE (focus protection) and WS_EX_TOOLWINDOW (Alt-Tab exclusion)
            WindowInteropHelpers.InitializeOverlayStyles(_hwnd, _viewModel.IsClickThrough);
            UpdateUiForClickThrough(_viewModel.IsClickThrough);
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null && _viewModel.OverlayLeft.HasValue && _viewModel.OverlayTop.HasValue)
        {
            _isApplyingBounds = true;
            try
            {
                Left = _viewModel.OverlayLeft.Value;
                Top = _viewModel.OverlayTop.Value;
                if (_viewModel.OverlayWidth.HasValue && _viewModel.OverlayWidth > 0)
                    Width = _viewModel.OverlayWidth.Value;
                if (_viewModel.OverlayHeight.HasValue && _viewModel.OverlayHeight > 0)
                    Height = _viewModel.OverlayHeight.Value;
            }
            finally
            {
                _isApplyingBounds = false;
            }
        }
        else
        {
            CenterOverlayOnBottom();
        }
    }

    private void CenterOverlayOnBottom()
    {
        _isApplyingBounds = true;
        try
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            Width = 640;
            Height = 280;
            Left = (screenWidth - Width) / 2;
            Top = screenHeight - Height - 100; // Positioned nicely near bottom of screen
        }
        finally
        {
            _isApplyingBounds = false;
        }
    }

    private void OnLocationOrSizeChanged(object? sender, EventArgs e)
    {
        if (_isApplyingBounds || _viewModel == null)
            return;

        if (WindowState == WindowState.Normal && !double.IsNaN(Left) && !double.IsNaN(Top))
        {
            _viewModel.UpdateBounds(Left, Top, ActualWidth, ActualHeight);
        }
    }

    private void OnDragBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnOverlayVisibilityChanged(bool isVisible)
    {
        if (isVisible)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    private void OnClickThroughChanged(bool isClickThrough)
    {
        if (_hwnd != IntPtr.Zero)
        {
            WindowInteropHelpers.SetClickThrough(_hwnd, isClickThrough);
        }

        UpdateUiForClickThrough(isClickThrough);
    }

    private void UpdateUiForClickThrough(bool isClickThrough)
    {
        // When click-through is enabled, hide the drag bar and resize grip so it's a seamless HUD
        DragBar.Visibility = isClickThrough ? Visibility.Collapsed : Visibility.Visible;
        WindowResizeGrip.Visibility = isClickThrough ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnResetPositionRequested()
    {
        CenterOverlayOnBottom();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.RequestOverlayVisibilityChanged -= OnOverlayVisibilityChanged;
            _viewModel.RequestClickThroughChanged -= OnClickThroughChanged;
            _viewModel.RequestResetPosition -= OnResetPositionRequested;
        }
        base.OnClosed(e);
    }
}
