namespace ProtonBackup.Core;

public sealed class SystemdManager(string? executablePath = null)
{
    public const string ServiceName = "protonbackup-sync.service";
    public const string TimerName = "protonbackup-sync.timer";
    public const string SourceServiceTemplate = "protonbackup-sync@.service";

    private static readonly string UnitDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "systemd", "user");

    private readonly string _executable = executablePath ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the path to this program.");

    public static TimeSpan DefaultInterval => TimeSpan.FromMinutes(30);
    public static TimeSpan StartupDelay => TimeSpan.FromMinutes(2);

    public async Task InstallUnitsAsync(TimeSpan? interval = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(UnitDirectory);

        await File.WriteAllTextAsync(Path.Combine(UnitDirectory, ServiceName), $"""
            [Unit]
            Description=Proton Drive backup, een syncronde

            [Service]
            Type=oneshot
            ExecStart={_executable} --run-once

            """, token);

        await File.WriteAllTextAsync(Path.Combine(UnitDirectory, SourceServiceTemplate), $"""
            [Unit]
            Description=Proton Drive backup, een syncronde voor bron %i

            [Service]
            Type=oneshot
            ExecStart={_executable} --run-once --source %i

            """, token);

        await File.WriteAllTextAsync(Path.Combine(UnitDirectory, TimerName), $"""
            [Unit]
            Description=Proton Drive backup op een interval

            [Timer]
            OnActiveSec={FormatInterval(StartupDelay)}
            OnUnitActiveSec={FormatInterval(interval ?? DefaultInterval)}
            Unit={ServiceName}

            [Install]
            WantedBy=timers.target

            """, token);

        await ReloadAsync(token);
    }

    /// The interval comes from a drop-in so the unit itself stays untouched. The empty assignment
    /// is needed because systemd otherwise adds timer values instead of replacing them.
    /// In systemd an empty assignment clears the whole list of monotonic timers, so OnActiveSec must
    /// be added again afterwards. OnActiveSec counts from activation of the timer itself; OnStartupSec
    /// would already have expired after login, and systemd does not catch up on an expired monotonic timer.
    public async Task SetIntervalAsync(TimeSpan interval, CancellationToken token = default)
    {
        var directory = Path.Combine(UnitDirectory, TimerName + ".d");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "interval.conf"), $"""
            [Timer]
            OnUnitActiveSec=
            OnActiveSec={FormatInterval(StartupDelay)}
            OnUnitActiveSec={FormatInterval(interval)}

            """, token);
        await ReloadAsync(token);
    }

    public Task<CliResult> ReloadAsync(CancellationToken token = default) =>
        Systemctl(["daemon-reload"], token);

    public Task<CliResult> EnableTimerAsync(CancellationToken token = default) =>
        Systemctl(["enable", "--now", TimerName], token);

    public Task<CliResult> DisableTimerAsync(CancellationToken token = default) =>
        Systemctl(["disable", "--now", TimerName], token);

    public async Task<bool> IsTimerEnabledAsync(CancellationToken token = default) =>
        (await Systemctl(["is-enabled", TimerName], token)).StdOut.Trim() == "enabled";

    /// --no-block because with Type=oneshot systemctl otherwise waits for the whole run to finish.
    public Task<CliResult> StartSyncAsync(long? sourceId = null, CancellationToken token = default) =>
        Systemctl(["start", "--no-block", sourceId is null ? ServiceName : $"protonbackup-sync@{sourceId}.service"], token);

    public Task<CliResult> StopSyncAsync(CancellationToken token = default) =>
        Systemctl(["stop", ServiceName], token);

    public async Task<bool> IsSyncRunningAsync(CancellationToken token = default) =>
        (await Systemctl(["is-active", ServiceName], token)).StdOut.Trim() is "active" or "activating";

    public async Task<string> DescribeTimerAsync(CancellationToken token = default)
    {
        var result = await Systemctl(["list-timers", TimerName, "--no-pager"], token);
        return result.StdOut.Trim();
    }

    private static Task<CliResult> Systemctl(IEnumerable<string> arguments, CancellationToken token) =>
        ProcessRunner.RunAsync("systemctl", new[] { "--user" }.Concat(arguments), token);

    private static string FormatInterval(TimeSpan interval) =>
        interval.TotalMinutes >= 1 ? $"{(int)interval.TotalMinutes}min" : $"{(int)interval.TotalSeconds}s";
}
