using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtonBackup.Core;
using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.ViewModels;

public partial class SourceFoldersPageViewModel(BackupService service, IFolderPicker folderPicker)
    : PageViewModelBase(service)
{
    public override string Title => "Source folders";

    public ObservableCollection<SyncSource> Sources { get; } = [];

    [ObservableProperty] private string _newLocalPath = "";
    [ObservableProperty] private string _newRemotePath = "/my-files/Backup";
    [ObservableProperty] private string? _message;

    public override Task RefreshAsync()
    {
        var sources = Service.Database.GetSources();
        if (!sources.SequenceEqual(Sources))
        {
            Sources.Clear();
            foreach (var source in sources) Sources.Add(source);
        }
        return Task.CompletedTask;
    }

    /// Why a destination cannot be used, or null. "/" is refused: trimmed it would be an empty destination.
    internal static string? DestinationProblem(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination) || !destination.StartsWith('/'))
            return "The destination path must start with /, for example /my-files/Backup.";
        if (destination.TrimEnd('/').Length == 0)
            return "The destination must be a folder below /";
        return null;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await folderPicker.PickFolderAsync("Choose a source folder");
        if (picked is not null) NewLocalPath = picked;
    }

    [RelayCommand]
    private async Task AddSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(NewLocalPath) || !Directory.Exists(NewLocalPath))
        {
            Message = "Choose an existing folder first.";
            return;
        }
        if (DestinationProblem(NewRemotePath) is { } problem)
        {
            Message = problem;
            return;
        }

        Service.Database.AddSource(NewLocalPath, NewRemotePath.TrimEnd('/'));
        NewLocalPath = "";
        Message = "Source folder added.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveSourceAsync(SyncSource? source)
    {
        if (source is null) return;
        Service.Database.RemoveSource(source.Id);
        Message = "Source folder removed. Whatever is already on Proton stays there.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SyncSourceAsync(SyncSource? source)
    {
        if (source is null) return;
        var result = await Service.Systemd.StartSyncAsync(source.Id);
        Message = result.Ok ? $"Run started for {source.LocalPath}." : result.Output.Trim();
    }

    /// In case something was deleted on Proton: go through everything again.
    /// The CLI already skips what matches by content, so this is cheaper than it looks.
    [RelayCommand]
    private async Task ForceSourceAsync(SyncSource? source)
    {
        if (source is null) return;
        var reset = Service.Database.MarkAllPending(source.Id);
        var result = await Service.Systemd.StartSyncAsync(source.Id);
        Message = result.Ok
            ? $"{reset} file(s) queued again; the run has started."
            : result.Output.Trim();
    }
}
