namespace ProtonBackup.Core;

public sealed record CleanupStep(string Description, bool Succeeded, string? Detail = null);

/// Removes everything the app placed in your home folder. A package should not touch
/// that, so this happens from within the app and not from apt remove.
public static class Cleanup
{
    public static async Task<IReadOnlyList<CleanupStep>> RunAsync(CancellationToken token = default)
    {
        var steps = new List<CleanupStep>();
        var systemd = new SystemdManager(Environment.ProcessPath);

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

        steps.Add(Remove("Data folder removed", AppPaths.DataDir));
        steps.Add(Remove("Config folder removed", AppPaths.ConfigDir));

        var userUnits = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "systemd", "user");
        steps.Add(RemoveUnits(userUnits));

        await systemd.ReloadAsync(token);
        return steps;
    }

    private static CleanupStep Remove(string description, string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
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
            {
                var path = Path.Combine(directory, name);
                if (!File.Exists(path)) continue;
                File.Delete(path);
                removed.Add(name);
            }

            var dropIns = Path.Combine(directory, SystemdManager.TimerName + ".d");
            if (Directory.Exists(dropIns))
            {
                Directory.Delete(dropIns, recursive: true);
                removed.Add("drop-ins");
            }

            return new CleanupStep("systemd units cleaned up", true,
                removed.Count == 0 ? "nothing to remove" : string.Join(", ", removed));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CleanupStep("systemd units cleaned up", false, exception.Message);
        }
    }
}
