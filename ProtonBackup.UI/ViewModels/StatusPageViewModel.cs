using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtonBackup.Core;
using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.ViewModels;

public partial class StatusPageViewModel(BackupService service) : PageViewModelBase(service)
{
    public override string Title => "Status";

    [ObservableProperty] private string _lastRun = "no runs yet";
    [ObservableProperty] private string _nextRun = "no timer";
    [ObservableProperty] private string _sessionText = "unknown";
    [ObservableProperty] private string _cliVersion = "unknown";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _autoSync;
    [ObservableProperty] private int _syncedCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string? _availableVersion;
    [ObservableProperty] private bool _updating;

    private bool _applyingAutoSync;

    public override async Task RefreshAsync()
    {
        IsRunning = await Service.Systemd.IsSyncRunningAsync();

        _applyingAutoSync = true;
        AutoSync = await Service.Systemd.IsTimerEnabledAsync();
        _applyingAutoSync = false;

        var counts = Service.Database.GetStatusCounts();
        SyncedCount = counts.GetValueOrDefault(FileStatus.Synced);
        PendingCount = counts.GetValueOrDefault(FileStatus.Pending);
        ErrorCount = counts.GetValueOrDefault(FileStatus.Error);

        var runs = Service.Database.GetRecentRuns(1);
        LastRun = runs.Count == 0
            ? "no runs yet"
            : runs[0].FinishedUtc is { } finished
                ? $"{finished.ToLocalTime():dd-MM HH:mm} — {runs[0].Uploaded} uploaded, {runs[0].Failed} failed ({RunRowViewModel.Describe(runs[0].Result)})"
                : $"started {runs[0].StartedUtc.ToLocalTime():dd-MM HH:mm}, still running";

        NextRun = await Service.Systemd.NextRunAsync() ?? "no timer";
    }

    public async Task CheckForUpdateAsync(bool force = false)
    {
        var status = await Service.UpdateCheck.CheckAsync(force);
        if (status is null) return;
        AvailableVersion = status.Available;
        UpdateAvailable = status.UpdateAvailable && !status.Dismissed;
    }

    [RelayCommand]
    private async Task UpdateCliAsync()
    {
        if (await Service.Systemd.IsSyncRunningAsync())
        {
            Message = "A run is in progress and will finish first. Try again shortly.";
            return;
        }

        Updating = true;
        Message = "Downloading and verifying...";
        var release = await Service.Installer.FetchReleaseAsync();
        if (release is null)
        {
            Message = "The version page could not be read.";
            Updating = false;
            return;
        }

        var outcome = await Service.Installer.InstallAsync(release);
        Message = outcome.Message;
        Updating = false;
        if (outcome.Success) UpdateAvailable = false;
        Service.RefreshCliPath();
        await RefreshSlowAsync();
    }

    [RelayCommand]
    private async Task DismissUpdateAsync()
    {
        // The banner goes at once; the setting is written off the UI thread.
        UpdateAvailable = false;
        if (AvailableVersion is { } version) await Task.Run(() => Service.UpdateCheck.Dismiss(version));
    }

    public async Task RefreshSlowAsync()
    {
        CliVersion = await Service.GetCliVersionAsync();
        SessionText = await Service.GetSessionAsync() switch
        {
            SessionState.Active => "signed in",
            SessionState.Expired => "session expired",
            _ => "unknown",
        };
    }


    partial void OnAutoSyncChanged(bool value)
    {
        if (_applyingAutoSync) return;
        _ = ApplyAutoSyncAsync(value);
    }

    private async Task ApplyAutoSyncAsync(bool enabled)
    {
        var result = enabled
            ? await Service.Systemd.EnableTimerAsync()
            : await Service.Systemd.DisableTimerAsync();
        Message = result.Ok
            ? enabled ? "Automatic syncing is on." : "Automatic syncing is off."
            : result.Output.Trim();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (await Service.Systemd.IsSyncRunningAsync())
        {
            Message = "A run is already in progress.";
            return;
        }
        var result = await Service.Systemd.StartSyncAsync();
        Message = result.Ok ? "Run started." : result.Output.Trim();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        var result = await Service.Systemd.StopSyncAsync();
        Message = result.Ok ? "Stopped; the next run continues where this one left off." : result.Output.Trim();
        await RefreshAsync();
    }
}
