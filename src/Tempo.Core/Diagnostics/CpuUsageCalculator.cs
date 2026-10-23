namespace Tempo.Core.Diagnostics;

/// <summary>Turns successive GetSystemTimes samples into a usage percentage.</summary>
public sealed class CpuUsageCalculator
{
    private ulong _idle, _kernel, _user;
    private bool _hasSample;

    /// <param name="kernel">Kernel time, which (per GetSystemTimes) includes idle time.</param>
    /// <returns>Usage in percent since the previous sample, or null for the first sample.</returns>
    public double? Next(ulong idle, ulong kernel, ulong user)
    {
        if (!_hasSample || idle < _idle || kernel < _kernel || user < _user)
        {
            (_idle, _kernel, _user, _hasSample) = (idle, kernel, user, true);
            return null;
        }

        var dIdle = idle - _idle;
        var total = (kernel - _kernel) + (user - _user);
        (_idle, _kernel, _user) = (idle, kernel, user);

        if (total == 0)
            return 0;
        return Math.Clamp((double)(total - Math.Min(dIdle, total)) / total * 100, 0, 100);
    }
}
