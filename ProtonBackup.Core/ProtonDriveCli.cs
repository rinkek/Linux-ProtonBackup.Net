using System.Text.Json;

namespace ProtonBackup.Core;

public sealed class ProtonDriveCli(string executablePath)
{
    public string ExecutablePath { get; } = executablePath;

    /// Search order from the design: path from the settings, own data folder, then PATH.
    public static string? Locate(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var inDataDir = Path.Combine(AppPaths.BinDir, "proton-drive");
        if (File.Exists(inDataDir)) return inDataDir;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "proton-drive");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public Task<CliResult> RunAsync(IEnumerable<string> arguments, CancellationToken token = default) =>
        ProcessRunner.RunAsync(ExecutablePath, arguments, token);

    public async Task<string> GetVersionAsync(CancellationToken token = default)
    {
        var result = await RunAsync(["version"], token);
        return result.StdOut.Split('\n').FirstOrDefault()?.Trim() ?? "onbekend";
    }

    /// The CLI sometimes reports "You need to login first" while the session is perfectly valid, especially
    /// shortly after heavy traffic. Only after several failed attempts is the session considered expired,
    /// otherwise the daemon pauses wrongly and asks to log in again.
    public async Task<SessionState> CheckSessionAsync(CancellationToken token = default)
    {
        const int attempts = 3;
        var state = SessionState.Unknown;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var result = await RunAsync(["filesystem", "list", "/my-files", "--json"], token);
            if (result.Ok) return SessionState.Active;

            state = result.NotLoggedIn ? SessionState.Expired : SessionState.Unknown;
            if (attempt < attempts) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), token);
        }
        return state;
    }

    /// Returns null if the listing could not be fetched, so that a failed call is not
    /// distinguishable from an empty folder. This sometimes fails right after a large upload.
    public async Task<IReadOnlyList<RemoteNode>?> ListAsync(string remotePath, CancellationToken token = default)
    {
        var result = await RunAsync(["filesystem", "list", remotePath, "--json"], token);
        if (!result.Ok) return null;
        try
        {
            return RemoteNode.ParseList(result.StdOut);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string remotePath, CancellationToken token = default)
    {
        var result = await RunAsync(["filesystem", "info", remotePath, "--json"], token);
        return result.Ok;
    }

    /// Creates missing folders from top to bottom; the CLI does not do that itself on an upload.
    public async Task EnsureFolderAsync(string remotePath, CancellationToken token = default)
    {
        if (await ExistsAsync(remotePath, token)) return;

        var parent = RemotePath.ParentOf(remotePath);
        if (parent != "/" && parent != remotePath)
            await EnsureFolderAsync(parent, token);

        var result = await RunAsync(["filesystem", "create-folder", parent, RemotePath.NameOf(remotePath)], token);
        if (!result.Ok && !result.AlreadyExists)
            throw new IOException($"Could not create folder {remotePath}: {result.Output.Trim()}");
    }

    /// One call with multiple files is ~23x faster than per file (phase 1, test 3).
    /// The strategy flags are mandatory: without -f the CLI waits for input and, with
    /// stdin closed, silently skips the file with exit code 0.
    public Task<CliResult> UploadAsync(IEnumerable<string> localPaths, string remoteParent, CancellationToken token = default)
    {
        List<string> arguments =
        [
            "filesystem", "upload",
            "--file-conflict-strategy", "create-new-revision",
            "--folder-conflict-strategy", "merge",
            "--skip-thumbnails",
        ];
        arguments.AddRange(localPaths);
        arguments.Add(remoteParent);
        return RunAsync(arguments, token);
    }
}

public enum SessionState { Active, Expired, Unknown }
