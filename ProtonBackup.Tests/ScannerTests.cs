using ProtonBackup.Core;

namespace ProtonBackup.Tests;

public class ScannerTests
{
    private static readonly ScannedFile File = new("docs/een.txt", 100, 1_700_000_000_000, null);

    [Fact]
    public void UnknownFileNeedsUpload() =>
        Assert.True(Scanner.NeedsUpload(File, null));

    [Fact]
    public void UnchangedSyncedFileIsSkipped() =>
        Assert.False(Scanner.NeedsUpload(File, new TrackedFile(File.RelativePath, 100, 1_700_000_000_000, FileStatus.Synced)));

    [Fact]
    public void ChangedSizeNeedsUpload() =>
        Assert.True(Scanner.NeedsUpload(File, new TrackedFile(File.RelativePath, 99, 1_700_000_000_000, FileStatus.Synced)));

    [Fact]
    public void ChangedModificationTimeNeedsUpload() =>
        Assert.True(Scanner.NeedsUpload(File, new TrackedFile(File.RelativePath, 100, 1_600_000_000_000, FileStatus.Synced)));

    [Fact]
    public void PreviousErrorIsRetried() =>
        Assert.True(Scanner.NeedsUpload(File, new TrackedFile(File.RelativePath, 100, 1_700_000_000_000, FileStatus.Error)));

    [Fact]
    public void RecentlyWrittenFileIsLeftForTheNextRound()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            System.IO.File.WriteAllText(Path.Combine(directory.FullName, "vers.txt"), "net geschreven");
            Assert.Empty(new Scanner().Scan(directory.FullName));
            Assert.Single(new Scanner { SettleTime = TimeSpan.Zero }.Scan(directory.FullName));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void OnlyRegularFilesAreListed()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            System.IO.File.WriteAllText(Path.Combine(directory.FullName, "gewoon.txt"), "inhoud");
            var pipe = Path.Combine(directory.FullName, "pijp");
            using (var mkfifo = System.Diagnostics.Process.Start("mkfifo", pipe)) mkfifo.WaitForExit();
            Assert.True(System.IO.File.Exists(pipe));

            var listed = new Scanner { SettleTime = TimeSpan.Zero }.Scan(directory.FullName).ToList();
            Assert.Equal("gewoon.txt", Assert.Single(listed).RelativePath);
        }
        finally { directory.Delete(recursive: true); }
    }
}
