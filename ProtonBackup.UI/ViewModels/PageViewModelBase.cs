using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.ViewModels;

public abstract class PageViewModelBase(BackupService service) : ViewModelBase
{
    protected BackupService Service { get; } = service;

    public abstract string Title { get; }

    /// Called periodically while the page is visible.
    public virtual Task RefreshAsync() => Task.CompletedTask;
}
