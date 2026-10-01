namespace ProtonBackup.Core;

public static class AppPaths
{
    private static volatile bool _retired;

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProtonBackup");

    public static string ConfigDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProtonBackup");

    /// ~/.cache/ProtonBackup: nothing the app cannot recreate.
    public static string CacheDir { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } cache
            ? cache
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
        "ProtonBackup");

    /// ~/.config/systemd/user: the user's own units and drop-ins.
    public static string UserUnitDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "systemd", "user");

    /// ~/.local/share/systemd/timers: where systemd keeps the stamp file of a timer.
    public static string TimerStampDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "systemd", "timers");

    public static string BinDir => Path.Combine(DataDir, "bin");
    public static string DatabasePath => Path.Combine(DataDir, "protonbackup.db");
    public static string LockPath => Path.Combine(DataDir, "sync.lock");

    public static bool IsRetired => _retired;

    public static void EnsureCreated()
    {
        if (_retired)
            throw new InvalidOperationException("The app's data has been removed; nothing may recreate it.");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(BinDir);
    }

    /// After this call EnsureCreated fails. Called when the app's data is being removed, so that the
    /// running window cannot bring back what the cleanup just deleted.
    public static void Retire() => _retired = true;
}
