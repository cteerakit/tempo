using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.App.Interop;
using Tempo.App.Shell;
using Tempo.Core.Diagnostics;

namespace Tempo.App.Widgets;

internal sealed class SystemSettings
{
    [DisplayName("Show CPU")]
    public bool ShowCpu { get; set; } = true;

    [DisplayName("Show memory")]
    public bool ShowMemory { get; set; } = true;

    [DisplayName("Show battery"), Description("Only shown on devices with a battery")]
    public bool ShowBattery { get; set; } = true;

    [DisplayName("Update every (seconds)"), Range(1, 30)]
    public double IntervalSeconds { get; set; } = 2;
}

internal sealed partial class SystemWidget(WidgetContext context, SystemSettings settings) : WidgetViewModelBase(context)
{
    private readonly CpuUsageCalculator _cpu = new();

    public SystemSettings Settings { get; } = settings;

    [ObservableProperty]
    public partial double CpuPercent { get; set; }

    [ObservableProperty]
    public partial double MemoryPercent { get; set; }

    [ObservableProperty]
    public partial string MemoryDetail { get; set; } = "";

    [ObservableProperty]
    public partial bool HasBattery { get; set; }

    [ObservableProperty]
    public partial double BatteryPercent { get; set; }

    [ObservableProperty]
    public partial string BatteryGlyph { get; set; } = "\uE83F";

    [ObservableProperty]
    public partial string BatteryDetail { get; set; } = "";

    public bool ShowBatteryItem => Settings.ShowBattery && HasBattery;

    partial void OnHasBatteryChanged(bool value) => OnPropertyChanged(nameof(ShowBatteryItem));

    public override void Start() => Every(TimeSpan.FromSeconds(Math.Max(1, Settings.IntervalSeconds)), Sample);

    [RelayCommand]
    private void OpenTaskManager()
    {
        IsFlyoutOpen = false;
        ShellActions.Launch("taskmgr.exe");
    }

    private void Sample()
    {
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)
            && _cpu.Next(idle.Value, kernel.Value, user.Value) is { } cpu)
            CpuPercent = Math.Round(cpu);

        var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref memory))
        {
            MemoryPercent = memory.dwMemoryLoad;
            var used = (memory.ullTotalPhys - memory.ullAvailPhys) / 1073741824.0;
            MemoryDetail = $"{used:0.0} of {memory.ullTotalPhys / 1073741824.0:0.0} GB in use";
        }

        if (NativeMethods.GetSystemPowerStatus(out var power))
        {
            // BatteryFlag 128 = no system battery, 255 = unknown.
            HasBattery = power.BatteryFlag is not (128 or 255) && power.BatteryLifePercent <= 100;
            if (HasBattery)
            {
                var percent = power.BatteryLifePercent;
                var charging = power.ACLineStatus == 1;
                var level = (int)Math.Round(percent / 10.0);
                BatteryPercent = percent;
                BatteryGlyph = charging
                    ? char.ConvertFromUtf32(0xEBAB + level)
                    : level >= 10 ? "\uE83F" : char.ConvertFromUtf32(0xE850 + level);
                BatteryDetail = charging
                    ? percent >= 100 ? "Fully charged" : "Charging"
                    : power.BatteryLifeTime > 0 ? $"{TimeSpan.FromSeconds(power.BatteryLifeTime):h\\:mm} remaining" : "On battery";
            }
        }
    }
}
