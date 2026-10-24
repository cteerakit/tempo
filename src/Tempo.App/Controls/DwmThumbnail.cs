using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Tempo.App.Interop;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Controls;

/// <summary>
/// A live preview of another top-level window, drawn by DWM (the same API the Windows taskbar uses).
/// DWM paints it over this control's area in the host window, so nothing can be layered on top of it.
/// </summary>
public sealed class DwmThumbnail : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(nint), typeof(DwmThumbnail), new PropertyMetadata((nint)0, (d, _) => ((DwmThumbnail)d).Reconnect()));

    public static readonly DependencyProperty MaxThumbnailWidthProperty = DependencyProperty.Register(
        nameof(MaxThumbnailWidth), typeof(double), typeof(DwmThumbnail),
        new FrameworkPropertyMetadata(220d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxThumbnailHeightProperty = DependencyProperty.Register(
        nameof(MaxThumbnailHeight), typeof(double), typeof(DwmThumbnail),
        new FrameworkPropertyMetadata(140d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private HwndSource? _host;
    private nint _thumbnail;

    public DwmThumbnail()
    {
        Loaded += (_, _) => Reconnect();
        Unloaded += (_, _) => Disconnect();
        LayoutUpdated += (_, _) => UpdateDestination();
    }

    /// <summary>The window to preview.</summary>
    public nint Source
    {
        get => (nint)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double MaxThumbnailWidth
    {
        get => (double)GetValue(MaxThumbnailWidthProperty);
        set => SetValue(MaxThumbnailWidthProperty, value);
    }

    public double MaxThumbnailHeight
    {
        get => (double)GetValue(MaxThumbnailHeightProperty);
        set => SetValue(MaxThumbnailHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Fit the source's aspect ratio inside the max box; 16:10 until DWM reports a size.
        var aspect = 1.6;
        if (_thumbnail != 0 && DwmQueryThumbnailSourceSize(_thumbnail, out var size) == 0 && size.cx > 0 && size.cy > 0)
            aspect = (double)size.cx / size.cy;
        var width = Math.Min(MaxThumbnailWidth, availableSize.Width);
        var height = width / aspect;
        if (height > MaxThumbnailHeight)
        {
            height = MaxThumbnailHeight;
            width = height * aspect;
        }
        return new Size(width, height);
    }

    private void Reconnect()
    {
        Disconnect();
        if (Source == 0 || !IsLoaded || PresentationSource.FromVisual(this) is not HwndSource host)
            return;
        if (DwmRegisterThumbnail(host.Handle, Source, out var thumbnail) != 0)
            return;
        _host = host;
        _thumbnail = thumbnail;
        InvalidateMeasure();
        UpdateDestination();
    }

    private void Disconnect()
    {
        if (_thumbnail != 0)
            DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = 0;
        _host = null;
    }

    private void UpdateDestination()
    {
        if (_thumbnail == 0 || _host?.RootVisual is not Visual root || !IsVisible)
            return;
        var toDevice = _host.CompositionTarget.TransformToDevice;
        var topLeft = toDevice.Transform(TransformToAncestor(root).Transform(new Point()));
        var bottomRight = toDevice.Transform(TransformToAncestor(root).Transform(new Point(ActualWidth, ActualHeight)));
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            Flags = DWM_THUMBNAIL_PROPERTIES.RectDestination | DWM_THUMBNAIL_PROPERTIES.Visible | DWM_THUMBNAIL_PROPERTIES.Opacity,
            Destination = new RECT((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y), (int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y)),
            Alpha = 255,
            IsVisible = 1,
        };
        DwmUpdateThumbnailProperties(_thumbnail, ref properties);
    }
}
