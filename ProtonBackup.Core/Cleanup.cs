namespace ProtonBackup.Core;

public sealed record CleanupStep(string Description, bool Succeeded, string? Detail = null);

/// Removes everything the app placed in your home folder. A package should not touch
/// that, so this happens from within the app and not from apt remove. Whatever is on
/// Proton Drive is never touched.
public static class Cleanup
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    /// <param name="quiesce">Stops background work and closes the database and lock; called before anything is deleted.</param>
    public static async Task<IReadOnlyList<CleanupStep>> RunAsync(Action? quiesce = null, CancellationToken token = default)
    {
        var steps = new List<CleanupStep>();
        var systemd = new SystemdManager(Environment.ProcessPath);

        // 1. The app must stop using its files before they are deleted, and must not bring them back.
        try
        {
            quiesce?.Invoke();
            AppPaths.Retire();
            steps.Add(new CleanupStep("Background work stopped", true));
        }
        catch (Exception exception)
        {
            AppPaths.Retire();
            steps.Add(new CleanupStep("Background work stopped", false, exception.Message));
        }

        // 2. systemd and the Proton session.
        var disable = await systemd.DisableTimerAsync(token);
        steps.Add(new CleanupStep("Timer turned off", disable.Ok, disable.Ok ? null : disable.Output.Trim()));

        var stop = await systemd.StopSyncAsync(token);
        steps.Add(new CleanupStep("Run in progress stopped", stop.Ok, stop.Ok ? null : stop.Output.Trim()));

        var cliPath = ProtonDriveCli.Locate(null);
        if (cliPath is not null)
        {
            var logout = await ProcessRunner.RunAsync(cliPath, ["auth", "logout"], token);
            steps.Add(new CleanupStep("Signed out of Proton", logout.Ok, logout.Ok ? null : logout.Output.Trim()));
        }
        else
        {
            steps.Add(new CleanupStep("Sign-out skipped", true, "the CLI was not found"));
        }

        // 3. Files.
        steps.Add(Remove("Data folder removed", AppPaths.DataDir));
        steps.Add(Remove("Config folder removed", AppPaths.ConfigDir));
        steps.Add(Remove("Cache folder removed", AppPaths.CacheDir));
        steps.Add(RemoveUnits(AppPaths.UserUnitDir));
        steps.Add(RemoveTimerStamps());

        // 4. Tell systemd the units are gone.
        var reload = await systemd.ReloadAsync(token);
        steps.Add(new CleanupStep("systemd reloaded", reload.Ok, reload.Ok ? null : reload.Output.Trim()));

        // 5. Prove it: look at the disk again.
        steps.Add(await VerifyAsync(token));
        return steps;
    }

    /// True when every step succeeded.
    public static bool Succeeded(IEnumerable<CleanupStep> steps) => steps.All(step => step.Succeeded);

    private static CleanupStep Remove(string description, string path)
    {
        try
        {
            DeleteTree(path);
            return new CleanupStep(description, true, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CleanupStep(description, false, exception.Message);
        }
    }

    /// Only the units and drop-ins in the home folder; what the package itself installed is left to apt.
    private static CleanupStep RemoveUnits(string directory)
    {
        var removed = new List<string>();
        try
        {
            foreach (var name in new[] { SystemdManager.ServiceName, SystemdManager.TimerName, SystemdManager.SourceServiceTemplate })
                if (DeleteIfPresent(Path.Combine(directory, name))) removed.Add(name);

            if (DeleteIfPresent(Path.Combine(directory, SystemdManager.TimerName + ".d"))) removed.Add("drop-ins");
            if (DeleteIfPresent(EnableLink(directory))) removed.Add("enable link");

            return new CleanupStep("systemd units cleaned up", true,
                removed.Count == 0 ? "nothing to remove" : string.Join(", ", removed));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CleanupStep("systemd units cleaned up", false, exception.Message);
        }
    }

    /// systemctl enable links the timer from timers.target.wants.
    private static string EnableLink(string unitDirectory) =>
        Path.Combine(unitDirectory, "timers.target.wants", SystemdManager.TimerName);

    private static string TimerStamp => Path.Combine(AppPaths.TimerStampDir, "stamp-" + SystemdManager.TimerName);

    private static CleanupStep RemoveTimerStamps()
    {
        try
        {
            var removed = DeleteIfPresent(TimerStamp);
            return new CleanupStep("Timer stamp file removed", true, removed ? "removed" : "nothing to remove");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CleanupStep("Timer stamp file removed", false, exception.Message);
        }
    }

    /// Everything that is ours and still on disk.
    private static List<string> Leftovers()
    {
        var unitDirectory = AppPaths.UserUnitDir;
        var candidates = new List<string> { AppPaths.DataDir, AppPaths.ConfigDir, AppPaths.CacheDir };
        candidates.AddRange(new[] { SystemdManager.ServiceName, SystemdManager.TimerName, SystemdManager.SourceServiceTemplate }
            .Select(name => Path.Combine(unitDirectory, name)));
        candidates.Add(Path.Combine(unitDirectory, SystemdManager.TimerName + ".d"));
        candidates.Add(EnableLink(unitDirectory));
        candidates.Add(TimerStamp);
        return candidates.Where(Exists).ToList();
    }

    private static async Task<CleanupStep> VerifyAsync(CancellationToken token)
    {
        var remaining = Leftovers();
        if (remaining.Count > 0)
        {
            // A file that was still being closed can be gone a moment later: try once more.
            await Task.Delay(RetryDelay, token);
            foreach (var path in remaining)
            {
                try { DeleteTree(path); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* reported below */ }
            }
            remaining = Leftovers();
        }

        return remaining.Count == 0
            ? new CleanupStep("Verified that nothing is left behind", true)
            : new CleanupStep("Verified that nothing is left behind", false, "still present: " + string.Join(", ", remaining));
    }

    /// A symlink counts as present even when it dangles.
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private static bool DeleteIfPresent(string path)
    {
        if (!Exists(path)) return false;
        DeleteTree(path);
        return true;
    }

    /// Deletes a file, symlink or folder tree; a symlink is removed, never followed.
    private static void DeleteTree(string path)
    {
        if (!Exists(path)) return;
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || File.Exists(path)) { File.Delete(path); return; }
        Directory.Delete(path, recursive: true);
    }
}
