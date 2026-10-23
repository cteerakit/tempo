using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Shell;

/// <summary>A click-through composition window below the WPF bar, with the same physical-pixel outline.</summary>
internal sealed class BarGlass : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct QueueOptions { public int Size, ThreadType, Apartment; }
    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(QueueOptions options, out nint controller);
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out nint target);
    }

    private static Windows.System.DispatcherQueueController? _queue;
    private readonly HwndSource _window;
    private readonly nint _bar;
    private readonly Compositor _compositor;
    private readonly DesktopWindowTarget _target;
    private readonly SpriteVisual _surface;
    private readonly CompositionRoundedRectangleGeometry _shape;
    private readonly CompositionGeometricClip _clip;
    private readonly CompositionBackdropBrush _brush;

    public BarGlass(nint hwnd)
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(new QueueOptions
            {
                Size = Marshal.SizeOf<QueueOptions>(), ThreadType = 2, Apartment = 2,
            }, out var queue));
            try { _queue = MarshalInterface<Windows.System.DispatcherQueueController>.FromAbi(queue); }
            finally { Marshal.Release(queue); }
        }
        _bar = hwnd;
        _window = new HwndSource(new HwndSourceParameters("Tempo glass")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            Width = 1, Height = 1,
        });
        _window.RootVisual = new System.Windows.Media.DrawingVisual();
        _window.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
        WindowEffects.MakeToolbarWindow(_window.Handle);
        WindowEffects.ClearShape(_window.Handle);
        WindowEffects.SetBackdrop(_window.Handle, Tempo.Core.Settings.BackdropKind.None);
        _compositor = new Compositor();
        _compositor.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(_window.Handle, true, out var target);
        try { _target = MarshalInterface<DesktopWindowTarget>.FromAbi(target); }
        finally { Marshal.Release(target); }
        var enabled = 1;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(_window.Handle, DWMWA_USE_HOSTBACKDROPBRUSH, ref enabled, sizeof(int)));
        _surface = _compositor.CreateSpriteVisual();
        _brush = _compositor.CreateHostBackdropBrush();
        _surface.Brush = _brush;
        _shape = _compositor.CreateRoundedRectangleGeometry();
        _clip = _compositor.CreateGeometricClip(_shape);
        _surface.Clip = _clip;
        _target.Root = _surface;
    }

    public void Update(int x, int y, int width, int height, double radius, double strength, bool visible)
    {
        _surface.Size = new Vector2(width, height);
        _shape.Size = _surface.Size;
        _shape.CornerRadius = new Vector2((float)radius);
        SetStrength(strength);
        _surface.IsVisible = visible;
        if (!visible)
        {
            Hide();
            return;
        }
        SetWindowPos(_window.Handle, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);
        ShowWindow(_window.Handle, SW_SHOWNA);

        SetWindowPos(_bar, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
        SetWindowPos(_window.Handle, _bar, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
    }

    public void SetStrength(double strength) => _surface.Opacity = (float)(strength / 100);

    public void Hide() => ShowWindow(_window.Handle, SW_HIDE);

    public void Dispose()
    {
        _target.Root = null;
        _surface.Dispose();
        _clip.Dispose();
        _shape.Dispose();
        _brush.Dispose();
        _target.Dispose();
        _compositor.Dispose();
        _window.Dispose();
    }
}
