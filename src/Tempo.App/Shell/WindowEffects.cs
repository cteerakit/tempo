using System.Runtime.InteropServices;
using Tempo.App.Interop;
using Tempo.Core.Settings;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Shell;

/// <summary>Window chrome tweaks: tool-window styles, blur behind, and corner shaping.</summary>
internal static class WindowEffects
{
    /// <summary>Keeps the window out of Alt+Tab and stops clicks from stealing focus from the user's app.</summary>
    public static void MakeToolbarWindow(nint hwnd)
    {
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex = (ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)ex);
    }

    /// <summary>
    /// Uses the accent-policy blur rather than DWM system backdrops: system backdrops turn into a solid
    /// colour whenever the window is inactive, and the bar is never active.
    /// </summary>
    public static void SetBackdrop(nint hwnd, BackdropKind kind)
    {
        // Lets WPF's per-pixel alpha show through to the desktop (or to the blur).
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        if (kind != BackdropKind.None)
            SetAlphaComposition(hwnd, false);

        var policy = new AccentPolicy
        {
            AccentState = kind switch
            {
                BackdropKind.Blur => ACCENT_ENABLE_BLURBEHIND,
                BackdropKind.Acrylic => ACCENT_ENABLE_ACRYLICBLURBEHIND,
                _ => ACCENT_DISABLED,
            },
            // Acrylic misbehaves with a fully transparent tint; WPF paints the real tint on top.
            GradientColor = 0x01000000,
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        if (kind == BackdropKind.None)
            SetAlphaComposition(hwnd, true);
    }

    private static void SetAlphaComposition(nint hwnd, bool enabled)
    {
        // Disabling accent blur can leave alpha black over the desktop. Keep DWM's
        // alpha composition enabled with an empty blur region; WPF draws the tint.
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            var blur = new DWM_BLURBEHIND
            {
                Flags = 0x1 | 0x2, // DWM_BB_ENABLE | DWM_BB_BLURREGION
                Enable = enabled ? 1 : 0,
                Region = region,
            };
            DwmEnableBlurBehindWindow(hwnd, ref blur);
        }
        finally
        {
            DeleteObject(region);
        }
    }

    public static void SetDarkMode(nint hwnd, bool dark)
    {
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    /// <summary>
    /// Keeps the window a plain rectangle: no window region (1-bit regions can't be antialiased) and no
    /// DWM rounding or border. Rounded shapes are drawn by WPF over the transparent window instead.
    /// </summary>
    public static void ClearShape(nint hwnd)
    {
        SetWindowRgn(hwnd, 0, true);
        var square = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));
        var noBorder = unchecked((int)DWMWA_COLOR_NONE);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
    }
}

