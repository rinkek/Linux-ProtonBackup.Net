using ProtonBackup.Core;

namespace ProtonBackup.Tests;

public class DatabaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"protonbackup-test-{Guid.NewGuid():N}.db");
    private readonly Database _database;

    public DatabaseTests() => _database = new Database(_path);

    [Fact]
    public void AddingTheSameSourceTwiceUpdatesTheTarget()
    {
        var first = _database.AddSource("/tmp/bron", "/my-files/A");
        var second = _database.AddSource("/tmp/bron", "/my-files/B");
        Assert.Equal(first, second);
        Assert.Equal("/my-files/B", Assert.Single(_database.GetSources()).RemotePath);
    }

    [Fact]
    public void PendingFilesBecomeSyncedAndStaySkipped()
    {
        var source = _database.AddSource("/tmp/bron2", "/my-files/A");
        _database.UpsertPending(source, [new ScannedFile("a.txt", 10, 1000, 42)]);
        Assert.Single(_database.GetPending(source));

        _database.MarkSynced(source, ["a.txt"]);
        Assert.Empty(_database.GetPending(source));
        Assert.Equal(FileStatus.Synced, _database.GetTrackedFiles(source)["a.txt"].Status);
    }

    [Fact]
    public void FailedFilesAreRetriedInTheNextRound()
    {
        var source = _database.AddSource("/tmp/bron3", "/my-files/A");
        _database.UpsertPending(source, [new ScannedFile("a.txt", 10, 1000, null)]);
        _database.MarkError(source, ["a.txt"], "netwerkfout");
        Assert.Single(_database.GetPending(source));
    }

    [Fact]
    public void LocallyDeletedFilesAreMarkedMissing()
    {
        var source = _database.AddSource("/tmp/bron4", "/my-files/A");
        _database.UpsertPending(source, [new ScannedFile("weg.txt", 10, 1000, null), new ScannedFile("blijft.txt", 10, 1000, null)]);
        _database.MarkSynced(source, ["weg.txt", "blijft.txt"]);

        Assert.Equal(1, _database.MarkMissing(source, ["blijft.txt"]));
        Assert.Equal(FileStatus.Missing, _database.GetTrackedFiles(source)["weg.txt"].Status);
        Assert.Equal(FileStatus.Synced, _database.GetTrackedFiles(source)["blijft.txt"].Status);
    }

    [Fact]
    public void ForcingResetsSyncedFilesButLeavesMissingOnesAlone()
    {
        var source = _database.AddSource("/tmp/bron5", "/my-files/A");
        _database.UpsertPending(source, [new ScannedFile("a.txt", 10, 1000, null), new ScannedFile("weg.txt", 10, 1000, null)]);
        _database.MarkSynced(source, ["a.txt", "weg.txt"]);
        _database.MarkMissing(source, ["a.txt"]);

        Assert.Equal(1, _database.MarkAllPending(source));

        var tracked = _database.GetTrackedFiles(source);
        Assert.Equal(FileStatus.Pending, tracked["a.txt"].Status);
        Assert.Equal(FileStatus.Missing, tracked["weg.txt"].Status);
    }

    [Fact]
    public void ForcingWithoutASourceCoversEverything()
    {
        var first = _database.AddSource("/tmp/bron6", "/my-files/A");
        var second = _database.AddSource("/tmp/bron7", "/my-files/B");
        _database.UpsertPending(first, [new ScannedFile("a.txt", 1, 1, null)]);
        _database.UpsertPending(second, [new ScannedFile("b.txt", 1, 1, null)]);
        _database.MarkSynced(first, ["a.txt"]);
        _database.MarkSynced(second, ["b.txt"]);

        Assert.Equal(2, _database.MarkAllPending());
        Assert.Single(_database.GetPending(first));
        Assert.Single(_database.GetPending(second));
    }

    public void Dispose()
    {
        _database.Dispose();
        File.Delete(_path);
    }
}
