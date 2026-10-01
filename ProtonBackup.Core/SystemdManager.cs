using System.Text.RegularExpressions;

namespace ProtonBackup.Core;

public sealed partial class SystemdManager(string? executablePath = null)
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
            Description=Proton Drive backup, a sync run

            [Service]
            Type=oneshot
            ExecStart={ExecStartCommand(_executable)} --run-once

            """, token);

        await File.WriteAllTextAsync(Path.Combine(UnitDirectory, SourceServiceTemplate), $"""
            [Unit]
            Description=Proton Drive backup, a sync run for source %i

            [Service]
            Type=oneshot
            ExecStart={ExecStartCommand(_executable)} --run-once --source %i

            """, token);

        await File.WriteAllTextAsync(Path.Combine(UnitDirectory, TimerName), $"""
            [Unit]
            Description=Proton Drive backup on an interval

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

    /// Whole minutes as Nmin, anything else in seconds (90 s must not silently become 1min).
    internal static string FormatInterval(TimeSpan interval)
    {
        var seconds = (long)interval.TotalSeconds;
        return seconds >= 60 && seconds % 60 == 0 ? $"{seconds / 60}min" : $"{seconds}s";
    }

    /// The program path as ExecStart wants it: a % would start a systemd specifier and whitespace
    /// would split the path into arguments.
    internal static string ExecStartCommand(string path)
    {
        var escaped = path.Replace("%", "%%");
        if (!escaped.Any(c => char.IsWhiteSpace(c) || c is '"' or '\\' or '\''))
            return escaped;
        return "\"" + escaped.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// When the timer fires next, as systemd prints it (for example Thu 2026-10-01 08:47:17 CEST), or null.
    public async Task<string?> NextRunAsync(CancellationToken token = default) =>
        ParseNextRun(await DescribeTimerAsync(token), TimerName);

    /// The NEXT column of systemctl list-timers. The table pads columns with a single space when a column
    /// is as wide as its widest cell, so splitting on double spaces can return several columns glued
    /// together; the timestamp itself is matched instead.
    internal static string? ParseNextRun(string listTimersOutput, string timerName)
    {
        foreach (var line in listTimersOutput.Split('\n'))
        {
            if (!line.Contains(timerName)) continue;
            var trimmed = line.Trim();
            if (trimmed.StartsWith('-')) return null; // "n/a": the timer is not scheduled
            var match = NextRunPattern().Match(trimmed);
            if (match.Success) return match.Value;
            var first = Regex.Split(trimmed, @"\s{2,}")[0];
            return first.Length > 0 && first != "-" ? first : null;
        }
        return null;
    }

    [GeneratedRegex(@"^\S+ \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?: [A-Za-z0-9+:-]+)?")]
    private static partial Regex NextRunPattern();
}
