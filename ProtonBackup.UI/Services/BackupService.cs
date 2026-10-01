using ProtonBackup.Core;

namespace ProtonBackup.UI.Services;

/// Single access point to the core for the UI. The UI never uploads anything itself: it reads the database
/// and controls the daemon via systemd.
public sealed class BackupService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public Database Database { get; }
    public SystemdManager Systemd { get; }
    public CliSettings CliSettings { get; }
    public CliInstaller Installer { get; }
    public CliUpdateCheck UpdateCheck { get; }
    public string? CliPath { get; private set; }

    public BackupService()
    {
        AppPaths.EnsureCreated();
        Database = new Database();
        Systemd = new SystemdManager(DaemonPath);
        CliSettings = new CliSettings(Database);
        Installer = new CliInstaller(Database, _http);
        UpdateCheck = new CliUpdateCheck(Database, Installer);
        CliPath = ProtonDriveCli.Locate(Database.GetSetting("cli_path"));
    }

    /// The units must point to the daemon, not to the UI itself.
    private static string DaemonPath
    {
        get
        {
            string[] candidates =
            [
                Path.Combine(AppContext.BaseDirectory, "protonbackup"),
                "/usr/bin/protonbackup",
                Path.Combine(AppPaths.DataDir, "app", "protonbackup"),
            ];
            return candidates.FirstOrDefault(File.Exists) ?? candidates[^1];
        }
    }

    public ProtonDriveCli? Cli => CliPath is null ? null : new ProtonDriveCli(CliPath);

    public void RefreshCliPath() => CliPath = ProtonDriveCli.Locate(Database.GetSetting("cli_path"));

    public async Task<SessionState> GetSessionAsync(CancellationToken token = default)
    {
        var cli = Cli;
        if (cli is null) return SessionState.Unknown;
        var result = await cli.RunAsync(["filesystem", "list", "/my-files", "--json"], token);
        if (result.Ok) return SessionState.Active;
        return result.NotLoggedIn ? SessionState.Expired : SessionState.Unknown;
    }

    public async Task<string> GetCliVersionAsync(CancellationToken token = default)
    {
        var cli = Cli;
        return cli is null ? "not found" : await cli.GetVersionAsync(token);
    }

    public Task<CliResult> LoginAsync(CancellationToken token = default) =>
        Cli is { } cli ? cli.RunAsync(["auth", "login"], token) : Task.FromResult(new CliResult(1, "", "CLI not found"));

    public Task<CliResult> LogoutAsync(CancellationToken token = default) =>
        Cli is { } cli ? cli.RunAsync(["auth", "logout"], token) : Task.FromResult(new CliResult(1, "", "CLI not found"));

    /// One click from a fresh install: the CLI is not in the package, so signing in fetches it first when it is
    /// missing, with no detour through another page.
    public async Task<(bool Success, string Message)> SignInAsync(Action<string> status, CancellationToken token = default)
    {
        if (CliPath is null)
        {
            status("The Proton Drive CLI is not installed yet; downloading it first...");
            var release = await Installer.FetchReleaseAsync(token);
            if (release is null) return (false, "The version page could not be read.");
            var outcome = await Installer.InstallAsync(release, token: token);
            RefreshCliPath();
            if (!outcome.Success) return (false, outcome.Message);
            if (CliPath is null) return (false, "The CLI was installed, but could not be found afterwards.");
        }

        status("Your browser will open; finish signing in there.");
        var result = await LoginAsync(token);
        return result.Ok ? (true, "Signed in.") : (false, result.Output.Trim());
    }

    /// Removes everything the app put in the home folder and signs out. The database is closed first, so
    /// afterwards this service is unusable: the caller must stop using it.
    public Task<IReadOnlyList<CleanupStep>> RemoveEverythingAsync() => Cleanup.RunAsync(quiesce: Database.Dispose);

    public void Dispose()
    {
        Database.Dispose();
        _http.Dispose();
    }
}
