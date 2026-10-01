namespace ProtonBackup.Core;

public sealed class Scanner
{
    /// A file that was written just now is left until the next run,
    /// so that a half-written file is not uploaded.
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromSeconds(5);

    public IEnumerable<ScannedFile> Scan(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
            throw new DirectoryNotFoundException($"Source folder does not exist: {fullRoot}");

        var cutoff = DateTimeOffset.UtcNow - SettleTime;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var path in Directory.EnumerateFiles(fullRoot, "*", options))
        {
            var info = new FileInfo(path);
            var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (modified > cutoff) continue;

            var relative = Path.GetRelativePath(fullRoot, path);
            yield return new ScannedFile(relative, info.Length, modified.ToUnixTimeMilliseconds(), NativeFile.GetInode(path));
        }
    }

    /// New path, or a different size or mtime than in the table: upload again.
    public static bool NeedsUpload(ScannedFile scanned, TrackedFile? tracked) =>
        tracked is null
        || tracked.Status != FileStatus.Synced
        || tracked.Size != scanned.Size
        || tracked.ModifiedUnixMs != scanned.ModifiedUnixMs;
}
