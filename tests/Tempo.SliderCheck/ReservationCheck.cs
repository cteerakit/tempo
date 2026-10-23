using System.Reflection;
using System.Windows;
using Tempo.Core.Settings;

internal static partial class Program
{
    private static int CheckReservation(Assembly assembly, object services, object settings)
    {
        var tray = services.GetType().GetProperty("TrayIcons")!.GetValue(services)!;
        tray.GetType().GetMethod("GetArea")!.Invoke(tray, null);
        var monitors = assembly.GetType("Tempo.App.Shell.Monitors", true)!;
        object Work()
        {
            var monitor = monitors.GetMethod("Find")!.Invoke(null, [null])!;
            return monitor.GetType().GetProperty("WorkArea")!.GetValue(monitor)!;
        }
        int Coordinate(object rect, string name) => (int)rect.GetType().GetField(name)!.GetValue(rect)!;
        void Settle() { for (var tick = 0; tick < 75; tick++) { Pump(); Thread.Sleep(10); } }
        var saved = (TempoSettings)settings.GetType().GetProperty("Current")!.GetValue(settings)!;
        saved.Bar.Mode = BarMode.Pill;
        saved.Bar.Edge = Environment.GetCommandLineArgs().Contains("--bottom") ? BarEdge.Bottom : BarEdge.Top;
        saved.Appearance.ThemeId = "Glass";
        saved.Behavior.HideOnFullscreen = false;
        var bar = (Window)Activator.CreateInstance(assembly.GetType("Tempo.App.Bar.BarWindow", true)!, [services])!;
        var passed = true;
        try
        {
            bar.Show();
            foreach (var reserve in new[] { true, false, true })
            {
                settings.GetType().GetMethod("Update")!.Invoke(settings,
                    [(Action<TempoSettings>)(s => s.Bar.ReserveSpaceInPill = reserve)]);
                Settle();
                GetWindowRect(new System.Windows.Interop.WindowInteropHelper(bar).Handle, out var pill);
                var work = Work();
                var clear = saved.Bar.Edge == BarEdge.Top
                    ? Coordinate(work, "Top") >= pill.Bottom
                    : Coordinate(work, "Bottom") <= pill.Top;
                var ok = clear == reserve;
                passed &= ok;
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {saved.Bar.Edge} reserve={reserve} with tray listener active; pill={pill.Top}..{pill.Bottom}; work area={work}");
            }
            return passed ? 0 : 1;
        }
        finally
        {
            bar.Close();
            tray.GetType().GetMethod("Release")!.Invoke(tray, null);
            Settle();
        }
    }
}
