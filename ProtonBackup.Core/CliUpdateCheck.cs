namespace ProtonBackup.Core;

public sealed record UpdateStatus(string? Installed, string? Available, bool UpdateAvailable, bool Dismissed);

/// Checks daily whether a newer CLI is available. A failed check is only logged
/// and must never block a sync run.
public sealed class CliUpdateCheck(Database database, CliInstaller installer, Action<string>? log = null)
{
    private readonly CliSettings _settings = new(database);

    public static TimeSpan Interval => TimeSpan.FromDays(1);

    public bool IsDue()
    {
        var last = database.GetSetting("cli_last_check_utc");
        return !DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var when)
               || DateTime.UtcNow - when >= Interval;
    }

    public async Task<UpdateStatus?> CheckAsync(bool force = false, CancellationToken token = default)
    {
        if (!force && !IsDue()) return null;

        var installed = await installer.GetInstalledVersionAsync(token);
        var release = await installer.FetchReleaseAsync(token);
        database.SetSetting("cli_last_check_utc", DateTime.UtcNow.ToString("O"));

        if (release is null) return null;
        _settings.LastSeenVersion = release.Version;

        var newer = installed is not null && CliVersionPage.IsNewer(release.Version, installed);
        if (newer) log?.Invoke($"A newer Proton Drive CLI is available: {release.Version} (currently {installed}).");

        return new UpdateStatus(installed, release.Version, newer,
            newer && _settings.DismissedVersion == release.Version);
    }

    public void Dismiss(string version) => _settings.DismissedVersion = version;
}
