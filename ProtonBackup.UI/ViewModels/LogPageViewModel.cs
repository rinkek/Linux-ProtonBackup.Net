using System.Collections.ObjectModel;
using ProtonBackup.Core;
using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.ViewModels;

public partial class LogPageViewModel(BackupService service) : PageViewModelBase(service)
{
    public override string Title => "Log";

    public ObservableCollection<RunRowViewModel> Runs { get; } = [];
    public ObservableCollection<FileEntry> Failures { get; } = [];

    public override Task RefreshAsync()
    {
        var runs = Service.Database.GetRecentRuns();
        if (!runs.Select(r => r.Id).SequenceEqual(Runs.Select(r => r.Id)))
        {
            Runs.Clear();
            foreach (var run in runs) Runs.Add(new RunRowViewModel(run));
        }

        var failures = Service.Database.GetFailedFiles();
        if (!failures.Select(f => f.RelativePath).SequenceEqual(Failures.Select(f => f.RelativePath)))
        {
            Failures.Clear();
            foreach (var failure in failures) Failures.Add(failure);
        }
        return Task.CompletedTask;
    }
}
