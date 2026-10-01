namespace ProtonBackup.Core;

public sealed record SyncSource(long Id, string LocalPath, string RemotePath, bool Enabled);

public sealed record ScannedFile(string RelativePath, long Size, long ModifiedUnixMs, long? Inode);

public sealed record TrackedFile(string RelativePath, long Size, long ModifiedUnixMs, string Status);

public static class FileStatus
{
    public const string Pending = "pending";
    public const string Synced = "synced";
    public const string Error = "error";
    public const string Missing = "missing";
}

public sealed record RunRecord(long Id, DateTime StartedUtc, DateTime? FinishedUtc, int Uploaded, int Failed, string? Result);

public sealed record FileEntry(string RelativePath, string Name, long Size, string Status, DateTime? LastSyncUtc, string? LastError);

public sealed record FolderEntry(string Name, string RelativePath, int FileCount, int ErrorCount, int PendingCount);
