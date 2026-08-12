using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CutLocal.App.Controls;

/// <summary>Displays bounded before/after proxies with clipping, mask, zoom, and pan.</summary>
public partial class BeforeAfterViewer : UserControl
{
    /// <summary>Identifies the before-image dependency property.</summary>
    public static readonly DependencyProperty BeforeSourceProperty = DependencyProperty.Register(
        nameof(BeforeSource),
        typeof(ImageSource),
        typeof(BeforeAfterViewer),
        new PropertyMetadata(null, OnPreviewSourceChanged));

    /// <summary>Identifies the after-image dependency property.</summary>
    public static readonly DependencyProperty AfterSourceProperty = DependencyProperty.Register(
        nameof(AfterSource),
        typeof(ImageSource),
        typeof(BeforeAfterViewer),
        new PropertyMetadata(null, OnPreviewSourceChanged));

    /// <summary>Identifies the mask-image dependency property.</summary>
    public static readonly DependencyProperty MaskSourceProperty = DependencyProperty.Register(
        nameof(MaskSource),
        typeof(ImageSource),
        typeof(BeforeAfterViewer),
        new PropertyMetadata(null, OnPreviewSourceChanged));

    /// <summary>Identifies the mask visibility dependency property.</summary>
    public static readonly DependencyProperty IsMaskVisibleProperty = DependencyProperty.Register(
        nameof(IsMaskVisible),
        typeof(bool),
        typeof(BeforeAfterViewer),
        new PropertyMetadata(false, OnPreviewSourceChanged));

    private const double MinimumZoom = 0.1;
    private const double MaximumZoom = 4;
    private Point _panStart;
    private double _horizontalOffsetStart;
    private double _verticalOffsetStart;
    private int _pixelWidth = 640;
    private int _pixelHeight = 420;
    private double _zoom = 1;
    private bool _isFitMode = true;
    private bool _updatingZoom;
    private bool _isPanning;
    private bool _componentsReady;

    /// <summary>Initializes the comparison control.</summary>
    public BeforeAfterViewer()
    {
        InitializeComponent();
        _componentsReady = true;
        SetZoom(_zoom);
        Loaded += OnLoaded;
        SizeChanged += OnViewerSizeChanged;
    }

    /// <summary>Gets or sets the original-image proxy.</summary>
    public ImageSource? BeforeSource
    {
        get => (ImageSource?)GetValue(BeforeSourceProperty);
        set => SetValue(BeforeSourceProperty, value);
    }

    /// <summary>Gets or sets the processed-image proxy.</summary>
    public ImageSource? AfterSource
    {
        get => (ImageSource?)GetValue(AfterSourceProperty);
        set => SetValue(AfterSourceProperty, value);
    }

    /// <summary>Gets or sets the grayscale alpha-mask proxy.</summary>
    public ImageSource? MaskSource
    {
        get => (ImageSource?)GetValue(MaskSourceProperty);
        set => SetValue(MaskSourceProperty, value);
    }

    /// <summary>Gets or sets whether the mask replaces comparison rendering.</summary>
    public bool IsMaskVisible
    {
        get => (bool)GetValue(IsMaskVisibleProperty);
        set => SetValue(IsMaskVisibleProperty, value);
    }

    /// <summary>Fits the current proxy inside the viewport.</summary>
    public void FitToViewport()
    {
        if (!IsLoaded || Viewport.ActualWidth <= 1 || Viewport.ActualHeight <= 1)
        {
            return;
        }

        double availableWidth = Math.Max(1, Viewport.ActualWidth - 28);
        double availableHeight = Math.Max(1, Viewport.ActualHeight - 28);
        _isFitMode = true;
        SetZoom(Math.Clamp(
            Math.Min(availableWidth / _pixelWidth, availableHeight / _pixelHeight),
            MinimumZoom,
            MaximumZoom));
        Viewport.ScrollToHorizontalOffset(0);
        Viewport.ScrollToVerticalOffset(0);
    }

    private static void OnPreviewSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((BeforeAfterViewer)dependencyObject).RefreshSourceState();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshSourceState();
        FitToViewport();
    }

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isFitMode)
        {
            FitToViewport();
        }
    }

    private void RefreshSourceState()
    {
        BitmapSource? source = BeforeSource as BitmapSource ?? AfterSource as BitmapSource;
        bool hasSource = source is not null;
        bool hasComparison = BeforeSource is not null && AfterSource is not null;
        if (source is not null)
        {
            _pixelWidth = Math.Max(1, source.PixelWidth);
            _pixelHeight = Math.Max(1, source.PixelHeight);
        }

        bool showMask = IsMaskVisible && MaskSource is not null;
        BeforeImage.Visibility = hasSource && !showMask ? Visibility.Visible : Visibility.Collapsed;
        AfterImage.Visibility = AfterSource is not null && !showMask
            ? Visibility.Visible
            : Visibility.Collapsed;
        MaskImage.Visibility = showMask ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasSource ? Visibility.Collapsed : Visibility.Visible;
        ComparisonLabels.Visibility = hasComparison && !showMask
            ? Visibility.Visible
            : Visibility.Collapsed;
        SplitDivider.Visibility = hasComparison && !showMask
            ? Visibility.Visible
            : Visibility.Collapsed;
        SplitSlider.IsEnabled = hasComparison && !showMask;
        ApplyCanvasSize();
        UpdateSplit();
        if (_isFitMode)
        {
            FitToViewport();
        }
    }

    private void OnFitClicked(object sender, RoutedEventArgs e) => FitToViewport();

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_componentsReady || _updatingZoom)
        {
            return;
        }

        _isFitMode = false;
        SetZoom(e.NewValue / 100);
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        _updatingZoom = true;
        ZoomSlider.Value = _zoom * 100;
        ZoomText.Text = $"{_zoom * 100:F0}%";
        _updatingZoom = false;
        ApplyCanvasSize();
        UpdateSplit();
    }

    private void ApplyCanvasSize()
    {
        PreviewCanvas.Width = Math.Max(1, _pixelWidth * _zoom);
        PreviewCanvas.Height = Math.Max(1, _pixelHeight * _zoom);
    }

    private void OnSplitChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_componentsReady)
        {
            UpdateSplit();
        }
    }

    private void UpdateSplit()
    {
        if (BeforeSource is null || AfterSource is null)
        {
            BeforeImage.Clip = null;
            AfterImage.Clip = null;
            return;
        }

        double split = Math.Clamp(SplitSlider.Value, 0, 1);
        double splitX = PreviewCanvas.Width * split;
        BeforeImage.Clip = new RectangleGeometry(new Rect(
            0,
            0,
            splitX,
            PreviewCanvas.Height));
        AfterImage.Clip = new RectangleGeometry(new Rect(
            splitX,
            0,
            PreviewCanvas.Width - splitX,
            PreviewCanvas.Height));
        SplitDivider.Margin = new Thickness(splitX - 1, 0, 0, 0);
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (BeforeSource is null)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(Viewport);
        _horizontalOffsetStart = Viewport.HorizontalOffset;
        _verticalOffsetStart = Viewport.VerticalOffset;
        PreviewCanvas.CaptureMouse();
        PreviewCanvas.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = e.GetPosition(Viewport);
        Viewport.ScrollToHorizontalOffset(_horizontalOffsetStart - (current.X - _panStart.X));
        Viewport.ScrollToVerticalOffset(_verticalOffsetStart - (current.Y - _panStart.Y));
        e.Handled = true;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        PreviewCanvas.ReleaseMouseCapture();
        PreviewCanvas.Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        _isFitMode = false;
        SetZoom(_zoom * (e.Delta > 0 ? 1.1 : 0.9));
        e.Handled = true;
    }

    private void OnViewportScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0 || e.VerticalChange != 0)
        {
            _isFitMode = false;
        }
    }
}
