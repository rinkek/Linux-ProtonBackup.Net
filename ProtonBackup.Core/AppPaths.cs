namespace ProtonBackup.Core;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProtonBackup");

    public static string ConfigDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProtonBackup");

    public static string BinDir => Path.Combine(DataDir, "bin");
    public static string DatabasePath => Path.Combine(DataDir, "protonbackup.db");
    public static string LockPath => Path.Combine(DataDir, "sync.lock");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(BinDir);
    }
}
