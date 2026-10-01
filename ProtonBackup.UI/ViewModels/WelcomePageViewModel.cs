using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtonBackup.Core;
using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.ViewModels;

/// First start: the four things needed before anything can be backed up.
public partial class WelcomePageViewModel(BackupService service, IFolderPicker folderPicker) : ViewModelBase
{
    [ObservableProperty] private bool _hasCli;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private bool _hasSource;
    [ObservableProperty] private bool _timerEnabled;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string? _message;

    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private string _remotePath = "/my-files/Backup";

    public bool CanFinish => HasCli && IsLoggedIn && HasSource;

    public event EventHandler? Finished;

    public async Task RefreshAsync()
    {
        service.RefreshCliPath();
        HasCli = service.CliPath is not null;
        HasSource = service.Database.GetSources().Count > 0;
        TimerEnabled = await service.Systemd.IsTimerEnabledAsync();
        if (!HasCli) IsLoggedIn = false;
        OnPropertyChanged(nameof(CanFinish));
    }

    /// The session costs a process start, so it is probed on the slow schedule, not on every tick.
    public async Task RefreshSlowAsync()
    {
        IsLoggedIn = HasCli && await service.GetSessionAsync() == SessionState.Active;
        OnPropertyChanged(nameof(CanFinish));
    }

    [RelayCommand]
    private async Task DownloadCliAsync()
    {
        Busy = true;
        Message = "Downloading and verifying the checksum...";
        var release = await service.Installer.FetchReleaseAsync();
        if (release is null)
        {
            Message = "The version page could not be read. Check your internet connection.";
            Busy = false;
            return;
        }
        var outcome = await service.Installer.InstallAsync(release);
        Message = outcome.Message;
        Busy = false;
        await RefreshAsync();
    }

    /// Installs the CLI first when it is missing: one click, no detour.
    [RelayCommand]
    private async Task LoginAsync()
    {
        Busy = true;
        var outcome = await service.SignInAsync(text => Message = text);
        Message = outcome.Message;
        Busy = false;
        await RefreshAsync();
        await RefreshSlowAsync();
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (await folderPicker.PickFolderAsync("Choose the folder you want to back up") is { } picked)
            LocalPath = picked;
    }

    [RelayCommand]
    private async Task AddSourceAsync()
    {
        if (!Directory.Exists(LocalPath))
        {
            Message = "Choose an existing folder first.";
            return;
        }
        var problem = SourceFoldersPageViewModel.DestinationProblem(RemotePath);
        if (problem is not null)
        {
            Message = problem;
            return;
        }
        service.Database.AddSource(LocalPath, RemotePath.TrimEnd('/'));
        Message = "Source folder added.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task EnableTimerAsync()
    {
        var result = await service.Systemd.EnableTimerAsync();
        Message = result.Ok ? "Automatic syncing is on." : result.Output.Trim();
        await RefreshAsync();
    }

    [RelayCommand]
    private void Finish()
    {
        service.Database.SetSetting("setup_completed", "1");
        Finished?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsNeeded(BackupService service) =>
        service.Database.GetSetting("setup_completed") != "1";
}
