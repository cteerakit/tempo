#nullable enable
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Tempo.Core.Shell;

internal static partial class Program
{
    private static int CheckNativeApps(Assembly assembly)
    {
        var trackerType = assembly.GetType("Tempo.App.Shell.WindowTracker", true)!;
        using var tracker = (IDisposable)Activator.CreateInstance(trackerType)!;
        var ready = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var helperThread = new Thread(() =>
        {
            using var hidden = new HwndSource(new HwndSourceParameters("Tempo blocked startup helper") { Width = 0, Height = 0 });
            ready.Set();
            release.Wait();
        }) { IsBackground = true };
        helperThread.SetApartmentState(ApartmentState.STA);
        helperThread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5))) throw new Exception("Helper setup timed out.");
        using (var timeout = new Timer(_ => release.Set(), null, 2000, Timeout.Infinite))
        {
            try
            {
                trackerType.GetMethod("GetWindows")!.Invoke(tracker, null);
                if (release.IsSet) throw new Exception("Tracking blocked on a hidden same-process startup helper.");
            }
            finally { release.Set(); helperThread.Join(3000); }
        }
        Console.WriteLine("PASS: tracking skips hidden helpers whose threads are blocked during startup.");
        var settings = new Window { Title = "Tempo settings", Width = 400, Height = 200, ShowActivated = false };
        var helper = new Window { Title = "Tempo bar helper", ShowInTaskbar = false, Width = 100, Height = 50, ShowActivated = false };
        try
        {
            settings.Show();
            helper.Show();
            Pump();
            var handles = ((IEnumerable)trackerType.GetMethod("GetWindows")!.Invoke(tracker, null)!).Cast<object>()
                .Select(w => (nint)w.GetType().GetProperty("Hwnd")!.GetValue(w)!).ToHashSet();
            if (!handles.Contains(new WindowInteropHelper(settings).Handle))
                throw new Exception("Tempo settings is missing from the running apps list.");
            if (handles.Contains(new WindowInteropHelper(helper).Handle))
                throw new Exception("Tempo helper incorrectly appears in the running apps list.");
            const string chrome = @"C:\Apps\chrome.exe";
            if (AppGrouping.MatchesPin(null, chrome, "Chrome.Profile.1", chrome))
                throw new Exception("An unidentified Chrome pin absorbs a different profile.");
            var hwnd = new WindowInteropHelper(settings).Handle;
            void SetIdentity(string identity, string property = "AppUserModel_ID")
            {
                var native = assembly.GetType("Tempo.App.Interop.NativeMethods", true)!;
                var storeType = assembly.GetType("Tempo.App.Interop.IPropertyStore", true)!;
                object[] args = [hwnd, storeType.GUID, null!];
                var hr = (int)native.GetMethod("SHGetPropertyStoreForWindow")!.Invoke(null, args)!;
                Marshal.ThrowExceptionForHR(hr);
                var store = args[2];
                var variantType = assembly.GetType("Tempo.App.Interop.PROPVARIANT", true)!;
                var variant = Activator.CreateInstance(variantType)!;
                var pointer = Marshal.StringToCoTaskMemUni(identity);
                try
                {
                    variantType.GetField("vt")!.SetValue(variant, (ushort)31);
                    variantType.GetField("pointerValue")!.SetValue(variant, pointer);
                    var key = assembly.GetType("Tempo.App.Interop.PROPERTYKEY", true)!.GetField(property)!.GetValue(null)!;
                    Marshal.ThrowExceptionForHR((int)storeType.GetMethod("SetValue")!.Invoke(store, [key, variant])!);
                }
                finally { Marshal.FreeCoTaskMem(pointer); Marshal.ReleaseComObject(store); }
            }
            string? ReadIdentity() => ((IEnumerable)trackerType.GetMethod("GetWindows")!.Invoke(tracker, null)!).Cast<object>()
                .Where(w => (nint)w.GetType().GetProperty("Hwnd")!.GetValue(w)! == hwnd)
                .Select(w => (string?)w.GetType().GetProperty("Aumid")!.GetValue(w)).Single();
            var liveWindows = ((IEnumerable)trackerType.GetMethod("GetWindows")!.Invoke(tracker, null)!).Cast<object>().ToList();
            var browserIds = liveWindows.Select(w => (string?)w.GetType().GetProperty("Aumid")!.GetValue(w))
                .Where(id => AppGrouping.IsBrowserIdentity(id)).Distinct().ToList();
            var shortcuts = assembly.GetType("Tempo.App.Shell.PinnedApps", true)!.GetMethod("FindAppShortcut")!;
            Console.WriteLine($"Live browser identities: {browserIds.Count}; matching installed shortcuts: {browserIds.Count(id => shortcuts.Invoke(null, [id]) is string)}.");
            SetIdentity("Chrome.Profile.1");
            if (ReadIdentity() != "Chrome.Profile.1") throw new Exception("Window profile identity was not read.");
            SetIdentity("Chrome.Profile.2");
            if (ReadIdentity() != "Chrome.Profile.2") throw new Exception("Window retained a stale profile identity.");
            var testId = "Chrome.TempoCheck." + Guid.NewGuid().ToString("N");
            SetIdentity(testId);
            SetIdentity("\"C:\\Apps\\chrome.exe\" --profile-directory=\"Profile 2\"", "AppUserModel_RelaunchCommand");
            var pinsType = assembly.GetType("Tempo.App.Shell.PinnedApps", true)!;
            var launcher = (string?)pinsType.GetMethod("FindProfileShortcut")!.Invoke(null, [testId, hwnd]);
            if (launcher is null) throw new Exception("Profile relaunch shortcut was not preserved.");
            try
            {
                var pin = pinsType.GetMethod("FromTarget")!.Invoke(null, [launcher])!;
                if ((string?)pin.GetType().GetProperty("Aumid")!.GetValue(pin) != testId
                    || (string?)pin.GetType().GetProperty("Arguments")!.GetValue(pin) != "--profile-directory=\"Profile 2\"")
                    throw new Exception("Profile identity or launch arguments lost when pinning.");
            }
            finally { System.IO.File.Delete(launcher); }
            Console.WriteLine("PASS: profile relaunch shortcut retains identity and quoted arguments.");
            Console.WriteLine("PASS: same-process settings tracked, helper hidden, Chrome profiles separated, changed native window identity refreshed.");
            return 0;
        }
        finally { settings.Close(); helper.Close(); }
    }
}
