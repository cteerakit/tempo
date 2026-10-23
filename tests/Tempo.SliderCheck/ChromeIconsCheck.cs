#nullable enable
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class Program
{
    private static int CheckChromeIcons(Assembly assembly, object services)
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("tempo-profile-icons-");
        var windows = new List<Window>();
        var launchers = new List<string>();
        IDisposable? widget = null;
        var icons = assembly.GetType("Tempo.App.Shell.ShellIcons", true)!;
        var pins = assembly.GetType("Tempo.App.Shell.PinnedApps", true)!;
        var widgetType = assembly.GetType("Tempo.App.Widgets.AppsWidget", true)!;
        var ids = Enumerable.Range(0, 3).Select(_ => "Chrome.TempoIconCheck." + Guid.NewGuid().ToString("N")).ToArray();
        void SetProperty(Window window, uint property, string text)
        {
            var native = assembly.GetType("Tempo.App.Interop.NativeMethods", true)!;
            var storeType = assembly.GetType("Tempo.App.Interop.IPropertyStore", true)!;
            object[] args = [new WindowInteropHelper(window).Handle, storeType.GUID, null!];
            Marshal.ThrowExceptionForHR((int)native.GetMethod("SHGetPropertyStoreForWindow")!.Invoke(null, args)!);
            var key = Activator.CreateInstance(assembly.GetType("Tempo.App.Interop.PROPERTYKEY", true)!, [new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), property])!;
            var variantType = assembly.GetType("Tempo.App.Interop.PROPVARIANT", true)!;
            var variant = Activator.CreateInstance(variantType)!;
            var pointer = Marshal.StringToCoTaskMemUni(text);
            try
            {
                variantType.GetField("vt")!.SetValue(variant, (ushort)31);
                variantType.GetField("pointerValue")!.SetValue(variant, pointer);
                Marshal.ThrowExceptionForHR((int)storeType.GetMethod("SetValue")!.Invoke(args[2], [key, variant])!);
            }
            finally { Marshal.FreeCoTaskMem(pointer); Marshal.ReleaseComObject(args[2]); }
        }
        string WriteIcon(string name, Color color)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 32, 32));
            var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new System.IO.MemoryStream();
            encoder.Save(png);
            var path = System.IO.Path.Combine(directory.FullName, name + ".ico");
            using var file = System.IO.File.Create(path);
            using var writer = new System.IO.BinaryWriter(file);
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
            writer.Write((byte)32); writer.Write((byte)32); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((uint)png.Length); writer.Write((uint)22);
            writer.Write(png.ToArray());
            return path;
        }
        string Fingerprint(object? icon)
        {
            if (icon is not BitmapSource source) throw new Exception("Missing profile bitmap.");
            var normalized = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[normalized.PixelWidth * normalized.PixelHeight * 4];
            normalized.CopyPixels(pixels, normalized.PixelWidth * 4, 0);
            return Convert.ToHexString(SHA256.HashData(pixels));
        }
        try
        {
            var paths = new[] { WriteIcon("Profile, first", Colors.Red), WriteIcon("Profile second", Colors.Green), WriteIcon("Profile third", Colors.Blue) };
            var expectedPaths = paths.ToArray();
            for (var i = 0; i < ids.Length; i++)
            {
                var window = new Window { Title = "Tempo Chrome profile icon check", Width = 120, Height = 60, ShowActivated = false };
                windows.Add(window);
                window.Show();
                SetProperty(window, 2, $"\"{Environment.ProcessPath}\" --profile-directory=\"Profile {i}\"");
                SetProperty(window, 3, $"\"{paths[i]}\",0");
                SetProperty(window, 5, ids[i]);
                launchers.Add((string)pins.GetMethod("FindProfileShortcut")!.Invoke(null, [ids[i], new WindowInteropHelper(window).Handle])!);
            }
            foreach (var launcher in launchers)
                if (pins.GetMethod("ReadShortcutIconResource")!.Invoke(null, [launcher]) is not string)
                    throw new Exception("Generated profile shortcut did not retain its icon location.");
            Pump();
            var context = Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.WidgetContext", true)!, ["chrome-icon-check", services, (Action<object>)(_ => { })])!;
            var settings = Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.AppsSettings", true)!)!;
            settings.GetType().GetProperty("ShowTaskbarPins")!.SetValue(settings, false);
            widget = (IDisposable)Activator.CreateInstance(widgetType, [context, settings])!;
            widgetType.GetMethod("Start")!.Invoke(widget, null);
            void AssertIcons(string stage)
            {
                var entries = ((IEnumerable)widgetType.GetProperty("Entries")!.GetValue(widget)!).Cast<object>().ToList();
                var hashes = new HashSet<string>();
                for (var i = 0; i < ids.Length; i++)
                {
                    var entry = entries.Single(e => (string)e.GetType().GetProperty("Key")!.GetValue(e)! == "aumid:" + ids[i].ToLowerInvariant()
                        || ((string?)e.GetType().GetProperty("Key")!.GetValue(e)) == "pin:" + launchers[i].ToLowerInvariant());
                    var selected = entry.GetType().GetProperty("Icon")!.GetValue(entry);
                    var expected = icons.GetMethod("ForPath")!.Invoke(null, [expectedPaths[i], 24]);
                    if (Fingerprint(expected) != Fingerprint(selected))
                        throw new Exception($"{stage}: profile {i + 1} displays the generic/wrong Chrome icon.");
                    hashes.Add(Fingerprint(selected));
                }
                if (hashes.Count != ids.Length) throw new Exception($"{stage}: profile icons have collapsed to one image.");
                Console.WriteLine($"PASS: {stage}, all {ids.Length} profiles have their own icons.");
            }
            AssertIcons("running profiles without installed shortcuts");
            paths[1] = WriteIcon("Profile second changed", Colors.Yellow);
            expectedPaths[1] = paths[1];
            SetProperty(windows[1], 3, $"\"{paths[1]}\",0");
            widgetType.GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(widget, null);
            AssertIcons("changed profile icon");
            WriteIcon("Profile third", Colors.Purple);
            System.IO.File.SetLastWriteTimeUtc(paths[2], DateTime.UtcNow.AddSeconds(1));
            expectedPaths[2] = WriteIcon("Expected third changed", Colors.Purple);
            widgetType.GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(widget, null);
            AssertIcons("replaced profile icon file");
            settings.GetType().GetProperty("Pins")!.SetValue(settings, launchers.ToList());
            widgetType.GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(widget, null);
            AssertIcons("pinned running profiles");
            foreach (var window in windows) window.Close();
            Pump();
            widgetType.GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(widget, null);
            AssertIcons("pinned profiles after closing windows");
            widget.Dispose();
            widget = (IDisposable)Activator.CreateInstance(widgetType, [context, settings])!;
            widgetType.GetMethod("Start")!.Invoke(widget, null);
            AssertIcons("restored pins with no running profile windows");
            return 0;
        }
        finally
        {
            widget?.Dispose();
            foreach (var window in windows) window.Close();
            foreach (var launcher in launchers)
            {
                System.IO.File.Delete(launcher);
                System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(launcher)!);
            }
            foreach (var file in directory.GetFiles()) file.Delete();
            directory.Delete();
        }
    }
}
