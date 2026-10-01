using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ProtonBackup.Core;

namespace ProtonBackup.UI.ViewModels;

/// Node in the tree view. Children are only loaded when the folder is expanded,
/// so that tens of thousands of files do not slow down the UI.
public partial class FolderNodeViewModel : ObservableObject
{
    private readonly Database _database;
    private readonly long _sourceId;
    private bool _loaded;

    public FolderNodeViewModel(Database database, long sourceId, string name, string relativePath,
        int fileCount, int errorCount, int pendingCount, bool isFile = false, string? status = null,
        DateTime? lastSync = null, string? error = null)
    {
        _database = database;
        _sourceId = sourceId;
        Name = name;
        RelativePath = relativePath;
        IsFile = isFile;
        Status = status;
        LastSync = lastSync;
        Error = error;
        FileCount = fileCount;
        ErrorCount = errorCount;
        PendingCount = pendingCount;

        // A temporary node makes the folder look expandable before the contents are loaded.
        if (!isFile && fileCount > 0) Children.Add(new FolderNodeViewModel(database, sourceId, "loading\u2026", relativePath));
    }

    /// Only for the temporary node under a folder that is not loaded yet.
    private FolderNodeViewModel(Database database, long sourceId, string name, string relativePath)
    {
        _database = database;
        _sourceId = sourceId;
        _loaded = true;
        Name = name;
        RelativePath = relativePath;
        IsFile = true;
    }

    public string Name { get; }
    public string RelativePath { get; }
    public bool IsFile { get; }
    public string? Status { get; }
    public DateTime? LastSync { get; }
    public string? Error { get; }
    public int FileCount { get; }
    public int ErrorCount { get; }
    public int PendingCount { get; }

    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    public string Summary => IsFile
        ? Status switch
        {
            FileStatus.Synced => LastSync is { } when_ ? $"synced {when_.ToLocalTime():dd-MM HH:mm}" : "synced",
            FileStatus.Pending => "waiting to upload",
            FileStatus.Error => Error ?? "error",
            FileStatus.Missing => "gone locally",
            _ => Status ?? "",
        }
        : ErrorCount > 0
            ? $"{Counted(FileCount)}, {ErrorCount} failed"
            : PendingCount > 0
                ? $"{Counted(FileCount)}, {PendingCount} queued"
                : Counted(FileCount);

    private static string Counted(int count) => count == 1 ? "1 file" : $"{count} files";

    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _loaded || IsFile) return;
        _loaded = true;
        Children.Clear();
        foreach (var child in Load(_database, _sourceId, RelativePath))
            Children.Add(child);
    }

    public static IEnumerable<FolderNodeViewModel> Load(Database database, long sourceId, string relativeDirectory)
    {
        foreach (var folder in database.GetChildFolders(sourceId, relativeDirectory))
            yield return new FolderNodeViewModel(database, sourceId, folder.Name, folder.RelativePath,
                folder.FileCount, folder.ErrorCount, folder.PendingCount);

        foreach (var file in database.GetFilesIn(sourceId, relativeDirectory))
            yield return new FolderNodeViewModel(database, sourceId, file.Name, file.RelativePath,
                0, 0, 0, isFile: true, status: file.Status, lastSync: file.LastSyncUtc, error: file.LastError);
    }
}
