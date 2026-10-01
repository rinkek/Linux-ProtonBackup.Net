namespace ProtonBackup.Core;

public sealed record SyncResult(int Uploaded, int Failed, int Missing, string Outcome)
{
    public static SyncResult Blocked(string reason) => new(0, 0, 0, reason);
}
