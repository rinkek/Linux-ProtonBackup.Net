namespace ProtonBackup.Core;

/// Only one run at a time; a second start stops with a short message.
public sealed class SyncLock : IDisposable
{
    private readonly FileStream _stream;

    private SyncLock(FileStream stream) => _stream = stream;

    public static SyncLock? TryAcquire()
    {
        AppPaths.EnsureCreated();
        try
        {
            var stream = new FileStream(AppPaths.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(Environment.ProcessId);
            writer.Flush();
            return new SyncLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
