using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tempo.App.Interop;
using Tempo.Core.Shell;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Shell;

/// <summary>Loads crisp, alpha-correct icons for files, shell paths and windows; results are cached.</summary>
internal static class ShellIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static Guid _imageFactoryIid = typeof(IShellItemImageFactory).GUID;
    private static readonly Dictionary<(IconResource Resource, int Size), (long Modified, ImageSource Image)> ResourceCache = [];

    public static ImageSource? ForResource(string? resource, int sizePx)
    {
        if (!IconResource.TryParse(resource, out var icon)) return null;
        try
        {
            var modified = File.GetLastWriteTimeUtc(icon.Path).Ticks;
            var key = (icon, sizePx);
            if (ResourceCache.TryGetValue(key, out var cached) && cached.Modified == modified)
                return cached.Image;
            var result = SHDefExtractIcon(icon.Path, icon.Index, 0, out var handle, 0, (uint)sizePx);
            try
            {
                if (result != 0 || handle == 0) return null;
                var image = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                ResourceCache[key] = (modified, image);
                return image;
            }
            finally { if (handle != 0) DestroyIcon(handle); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            return null;
        }
    }

    public static ImageSource? ForBrowserProfile(nint hwnd, string? shortcut, int sizePx)
    {
        var resource = hwnd == 0 ? null : WindowTracker.ReadWindowProperty(hwnd, PROPERTYKEY.AppUserModel_RelaunchIconResource);
        return ForResource(resource, sizePx)
               ?? (shortcut is null ? null : ForResource(PinnedApps.ReadShortcutIconResource(shortcut), sizePx))
               ?? (hwnd == 0 ? null : ForWindow(hwnd))
               ?? (shortcut is null ? null : ForPath(shortcut, sizePx));
    }

    /// <param name="parsingName">A file path, .lnk path, or shell path such as <c>shell:AppsFolder\{AUMID}</c>.</param>
    public static ImageSource? ForPath(string parsingName, int sizePx = 48)
    {
        var key = $"{parsingName}|{sizePx}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        ImageSource? image = null;
        try
        {
            SHCreateItemFromParsingName(parsingName, 0, ref _imageFactoryIid, out var factory);
            try
            {
                if (factory.GetImage(new SIZE { cx = sizePx, cy = sizePx }, SIIGBF_ICONONLY, out var hbitmap) == 0 && hbitmap != 0)
                {
                    try
                    {
                        image = FromHBitmap(hbitmap);
                    }
                    finally
                    {
                        DeleteObject(hbitmap);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException or UnauthorizedAccessException)
        {
            // Not every path has an icon (e.g. an uninstalled pinned app); callers fall back.
        }

        if (image is not null)
            Cache[key] = image;
        return image;
    }

    public static ImageSource? ForWebApp(string aumid, int sizePx, nint hwnd = 0)
    {
        var shortcut = PinnedApps.FindWebAppShortcut(aumid);
        return (shortcut is null ? null : ForPath(shortcut, sizePx))
               ?? ForPath($@"shell:AppsFolder\{aumid}", sizePx)
               ?? (hwnd == 0 ? null : ForWindow(hwnd));
    }

    /// <summary>The icon a window advertises for itself (not cached: windows can change their icon).</summary>
    public static ImageSource? ForWindow(nint hwnd)
    {
        nint icon = 0;
        if (SendMessageTimeout(hwnd, WM_GETICON, ICON_BIG, 0, SMTO_ABORTIFHUNG, 50, out var big) != 0)
            icon = big;
        if (icon == 0 && SendMessageTimeout(hwnd, WM_GETICON, ICON_SMALL2, 0, SMTO_ABORTIFHUNG, 50, out var small) != 0)
            icon = small;
        if (icon == 0)
            icon = GetClassLongPtr(hwnd, GCLP_HICON);
        if (icon == 0)
            return null;

        try
        {
            // The window owns the icon handle, so it is not destroyed here.
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static BitmapSource? FromHBitmap(nint hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0)
            return null;

        int width = bm.bmWidth, height = bm.bmHeight;
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var hdc = GetDC(0);
        try
        {
            if (GetDIBits(hdc, hbitmap, 0, (uint)height, pixels, ref header, 0) == 0)
                return null;
        }
        finally
        {
            ReleaseDC(0, hdc);
        }

        // Bitmaps without an alpha channel come back fully transparent; treat those as opaque.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        // The shell returns premultiplied or straight alpha depending on the icon source. In premultiplied
        // data no colour channel exceeds alpha; reading straight alpha as premultiplied makes bright edge fringes.
        var premultiplied = true;
        for (var i = 0; hasAlpha && i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (pixels[i] > a || pixels[i + 1] > a || pixels[i + 2] > a)
            {
                premultiplied = false;
                break;
            }
        }

        var format = !hasAlpha ? PixelFormats.Bgr32 : premultiplied ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
        var source = BitmapSource.Create(width, height, 96, 96, format, null, pixels, width * 4);
        source.Freeze();
        return source;
    }
}
