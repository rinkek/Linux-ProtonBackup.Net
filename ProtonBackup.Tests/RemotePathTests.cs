using ProtonBackup.Core;

namespace ProtonBackup.Tests;

public class RemotePathTests
{
    [Fact]
    public void CombineBuildsPosixPath() =>
        Assert.Equal("/my-files/Backup/docs/notities",
            RemotePath.Combine("/my-files/Backup", "docs/notities"));

    [Fact]
    public void CombineWithEmptyRelativePathReturnsParent() =>
        Assert.Equal("/my-files/Backup", RemotePath.Combine("/my-files/Backup", ""));

    [Fact]
    public void SlashInNameIsEscaped() =>
        Assert.Equal("/my-files/a\\/b", RemotePath.Join("/my-files", "a/b"));

    [Fact]
    public void ParentAndNameSplitThePath()
    {
        Assert.Equal("/my-files/docs", RemotePath.ParentOf("/my-files/docs/een.txt"));
        Assert.Equal("een.txt", RemotePath.NameOf("/my-files/docs/een.txt"));
    }
}
