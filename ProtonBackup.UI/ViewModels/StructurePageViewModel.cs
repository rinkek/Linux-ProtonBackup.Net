using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtonBackup.Core;
using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.ViewModels;

public partial class StructurePageViewModel(BackupService service) : PageViewModelBase(service)
{
    public override string Title => "File sync";

    public ObservableCollection<SyncSource> Sources { get; } = [];
    public ObservableCollection<FolderNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<FileEntry> Failures { get; } = [];

    [ObservableProperty] private SyncSource? _selectedSource;
    [ObservableProperty] private bool _onlyErrors;
    [ObservableProperty] private bool _hasSources;

    public override Task RefreshAsync()
    {
        var sources = Service.Database.GetSources();
        if (!sources.Select(s => s.Id).SequenceEqual(Sources.Select(s => s.Id)))
        {
            var previous = SelectedSource?.Id;
            Sources.Clear();
            foreach (var source in sources) Sources.Add(source);
            SelectedSource = Sources.FirstOrDefault(s => s.Id == previous) ?? Sources.FirstOrDefault();
        }
        HasSources = Sources.Count > 0;
        return Task.CompletedTask;
    }

    partial void OnSelectedSourceChanged(SyncSource? value) => Reload();

    partial void OnOnlyErrorsChanged(bool value) => Reload();

    [RelayCommand]
    private void Reload()
    {
        Nodes.Clear();
        Failures.Clear();
        if (SelectedSource is not { } source) return;

        if (OnlyErrors)
        {
            foreach (var failure in Service.Database.GetFailedFiles())
                Failures.Add(failure);
            return;
        }

        foreach (var node in FolderNodeViewModel.Load(Service.Database, source.Id, ""))
            Nodes.Add(node);
    }
}
