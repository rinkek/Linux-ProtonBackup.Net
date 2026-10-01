using System.Security.Cryptography;

namespace ProtonBackup.Core;

public sealed record InstallOutcome(bool Success, string Message, string? Version = null);

/// Fetches the CLI, verifies the checksum and puts it in place. The previous version is
/// kept, so that a failed session check after updating can be rolled back.
public sealed class CliInstaller(Database database, HttpClient http, Action<string>? log = null)
{
    private readonly CliSettings _settings = new(database);

    public string InstalledPath => Path.Combine(AppPaths.BinDir, "proton-drive");
    public string PreviousPath => InstalledPath + ".previous";

    private void Log(string message) => log?.Invoke(message);

    public async Task<CliRelease?> FetchReleaseAsync(CancellationToken token = default)
    {
        try
        {
            var html = await http.GetStringAsync(_settings.VersionPageUrl, token);
            var release = CliVersionPage.Parse(html);
            if (release is null) Log("The version page could not be read; its layout may have changed.");
            return release;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // A failed check must never block a sync; only log it.
            Log($"Update check failed: {exception.Message}");
            return null;
        }
    }

    public async Task<string?> GetInstalledVersionAsync(CancellationToken token = default)
    {
        if (!File.Exists(InstalledPath)) return null;
        var result = await ProcessRunner.RunAsync(InstalledPath, ["version"], token);
        if (!result.Ok) return null;
        var line = result.StdOut.Split('\n').FirstOrDefault() ?? "";
        var at = line.LastIndexOf('@');
        if (at < 0) return null;
        var version = line[(at + 1)..].Split('+')[0].Trim();
        return version.Length == 0 ? null : version;
    }

    public async Task<InstallOutcome> InstallAsync(CliRelease release, bool allowMissingChecksum = false,
        CancellationToken token = default)
    {
        var platform = _settings.Platform;
        var download = release.For(platform);
        var url = download?.Url ?? _settings.BuildDownloadUrl(release.Version, platform);

        if (download?.Sha512 is null && !_settings.SkipChecksum && !allowMissingChecksum)
            return new InstallOutcome(false,
                $"No checksum is listed for {platform}. Only proceed if you trust the source.");

        Directory.CreateDirectory(AppPaths.BinDir);
        var temporary = Path.Combine(Path.GetTempPath(), $"proton-drive-{release.Version}-{Guid.NewGuid():N}");

        try
        {
            Log($"Downloading {url}");
            await using (var source = await http.GetStreamAsync(url, token))
            await using (var target = File.Create(temporary))
                await source.CopyToAsync(target, token);

            if (download?.Sha512 is { } expected && !_settings.SkipChecksum)
            {
                var actual = await ComputeSha512Async(temporary, token);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(temporary);
                    return new InstallOutcome(false, "The checksum did not match; the file was not installed.");
                }
                Log("Checksum matches.");
            }

            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var check = await ProcessRunner.RunAsync(temporary, ["version"], token);
            if (IsIllegalInstruction(check) && platform == CliSettings.DefaultPlatform)
            {
                File.Delete(temporary);
                Log("This processor does not support the default build; switching to the baseline version.");
                _settings.Platform = CliSettings.BaselinePlatform;
                return await InstallAsync(release, allowMissingChecksum, token);
            }
            if (!check.Ok)
            {
                File.Delete(temporary);
                return new InstallOutcome(false, $"The downloaded CLI does not start: {check.Output.Trim()}");
            }

            var hadPrevious = File.Exists(InstalledPath);
            if (hadPrevious) File.Move(InstalledPath, PreviousPath, overwrite: true);
            File.Move(temporary, InstalledPath, overwrite: true);

            if (hadPrevious && !await SessionStillWorksAsync(token))
            {
                File.Move(PreviousPath, InstalledPath, overwrite: true);
                return new InstallOutcome(false,
                    "The session stopped working after updating; the previous version was restored.");
            }

            _settings.LastSeenVersion = release.Version;
            Log($"CLI {release.Version} installed.");
            return new InstallOutcome(true, $"CLI updated to {release.Version}.", release.Version);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            return new InstallOutcome(false, $"Download failed: {exception.Message}");
        }
    }

    public InstallOutcome Rollback()
    {
        if (!File.Exists(PreviousPath))
            return new InstallOutcome(false, "No previous version was kept.");

        File.Move(PreviousPath, InstalledPath, overwrite: true);
        return new InstallOutcome(true, "The previous version was restored.");
    }

    /// A list command shows whether the stored session still works with the new version.
    private async Task<bool> SessionStillWorksAsync(CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync(InstalledPath, ["filesystem", "list", "/my-files", "--json"], token);
        return result.Ok || !result.NotLoggedIn;
    }

    private static bool IsIllegalInstruction(CliResult result) =>
        result.ExitCode is 132 or -4 || result.Output.Contains("Illegal instruction", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ComputeSha512Async(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA512.HashDataAsync(stream, token);
        return Convert.ToHexStringLower(hash);
    }
}
