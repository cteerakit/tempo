using System.Windows.Threading;

namespace Tempo.App.Services;

/// <summary>
/// One shared UI-thread timer for every widget that polls, so idle CPU stays near zero.
/// Paused while the bar is hidden (for example during full-screen games).
/// </summary>
internal sealed class PollingScheduler
{
    private sealed class Job(TimeSpan interval, Action action) : IDisposable
    {
        public TimeSpan Interval { get; } = interval;
        public Action Action { get; } = action;
        public DateTime Due { get; set; }
        public PollingScheduler? Owner { get; set; }

        public void Dispose() => Owner?.Remove(this);
    }

    private readonly List<Job> _jobs = [];
    private readonly DispatcherTimer _timer;
    private bool _paused;

    public PollingScheduler(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick(), dispatcher) { IsEnabled = false };
    }

    /// <summary>Runs <paramref name="action"/> now and then every <paramref name="interval"/>. Dispose to stop.</summary>
    public IDisposable Every(TimeSpan interval, Action action, bool runNow = true)
    {
        var job = new Job(interval, action) { Owner = this, Due = DateTime.UtcNow + interval };
        _jobs.Add(job);
        UpdateTimer();
        if (runNow)
            Run(job);
        return job;
    }

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            UpdateTimer();
            if (!value)
                Tick();
        }
    }

    private void Remove(Job job)
    {
        job.Owner = null;
        _jobs.Remove(job);
        UpdateTimer();
    }

    private void UpdateTimer() => _timer.IsEnabled = !_paused && _jobs.Count > 0;

    private void Tick()
    {
        var now = DateTime.UtcNow;
        foreach (var job in _jobs.ToArray())
        {
            if (job.Owner is null || job.Due > now)
                continue;
            job.Due = now + job.Interval;
            Run(job);
        }
    }

    private static void Run(Job job)
    {
        try
        {
            job.Action();
        }
        catch (Exception ex)
        {
            Log.Error("Polling job failed", ex);
        }
    }
}
