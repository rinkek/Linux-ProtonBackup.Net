using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtonBackup.Core;
using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.ViewModels;

public partial class SettingsPageViewModel(BackupService service) : PageViewModelBase(service)
{
    public override string Title => "Settings";

    [ObservableProperty] private string _sessionText = "unknown";
    [ObservableProperty] private int _intervalMinutes = 30;
    [ObservableProperty] private string _cliPath = "";
    [ObservableProperty] private string _versionPageUrl = "";
    [ObservableProperty] private string _downloadTemplate = "";
    [ObservableProperty] private string _platform = "";
    [ObservableProperty] private bool _skipChecksum;
    [ObservableProperty] private string _installedVersion = "unknown";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _confirmCleanup;
    [ObservableProperty] private string? _message;

    public override Task RefreshAsync()
    {
        if (int.TryParse(Service.Database.GetSetting("interval_minutes"), out var minutes) && minutes != IntervalMinutes)
            IntervalMinutes = minutes;
        CliPath = Service.CliPath ?? "";
        VersionPageUrl = Service.CliSettings.VersionPageUrl;
        DownloadTemplate = Service.CliSettings.DownloadTemplate;
        Platform = Service.CliSettings.Platform;
        SkipChecksum = Service.CliSettings.SkipChecksum;
        return Task.CompletedTask;
    }

    public async Task RefreshSessionAsync() =>
        SessionText = await Service.GetSessionAsync() switch
        {
            SessionState.Active => "signed in",
            SessionState.Expired => "session expired",
            _ => "unknown",
        };

    /// Sign in is the very first thing shown on this page, with no earlier point
    /// where a first-time user would have had a reason to visit the CLI's own
    /// Update button - so a missing CLI is fetched here instead of just reporting
    /// "CLI not found", found confusing by actually using the app.
    [RelayCommand]
    private async Task LoginAsync()
    {
        Busy = true;
        if (Service.CliPath is null)
        {
            Message = "The Proton Drive CLI is not installed yet; downloading it first...";
            var release = await Service.Installer.FetchReleaseAsync();
            if (release is null)
            {
                Message = "The version page could not be read.";
                Busy = false;
                return;
            }
            var outcome = await Service.Installer.InstallAsync(release);
            Service.RefreshCliPath();
            if (!outcome.Success)
            {
                Message = outcome.Message;
                Busy = false;
                return;
            }
            if (Service.CliPath is null)
            {
                Message = "The CLI was installed, but could not be found afterwards.";
                Busy = false;
                return;
            }
            await RefreshAsync();
        }

        Message = "Your browser will open; finish signing in there.";
        var result = await Service.LoginAsync();
        Message = result.Ok ? "Signed in." : result.Output.Trim();
        Busy = false;
        await RefreshSessionAsync();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        Busy = true;
        var result = await Service.LogoutAsync();
        Message = result.Ok ? "Signed out." : result.Output.Trim();
        Busy = false;
        await RefreshSessionAsync();
    }

    [RelayCommand]
    private async Task ApplyIntervalAsync()
    {
        if (IntervalMinutes < 1)
        {
            Message = "The interval must be at least one minute.";
            return;
        }
        await Service.Systemd.SetIntervalAsync(TimeSpan.FromMinutes(IntervalMinutes));
        Service.Database.SetSetting("interval_minutes", IntervalMinutes.ToString());
        Message = $"Interval set to {IntervalMinutes} minutes.";
    }

    [RelayCommand]
    private async Task InstallUnitsAsync()
    {
        await Service.Systemd.InstallUnitsAsync(TimeSpan.FromMinutes(IntervalMinutes));
        Message = "systemd units written.";
    }

    [RelayCommand]
    private async Task SaveCliSettingsAsync()
    {
        Service.CliSettings.VersionPageUrl = VersionPageUrl;
        Service.CliSettings.DownloadTemplate = DownloadTemplate;
        Service.CliSettings.Platform = Platform;
        Service.CliSettings.SkipChecksum = SkipChecksum;
        Message = "CLI settings saved.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        Busy = true;
        var status = await Service.UpdateCheck.CheckAsync(force: true);
        Message = status is null
            ? "The version page could not be read."
            : status.UpdateAvailable
                ? $"Version {status.Available} is available (currently {status.Installed})."
                : $"You are up to date on version {status.Installed ?? status.Available}.";
        InstalledVersion = status?.Installed ?? "unknown";
        Busy = false;
    }

    [RelayCommand]
    private async Task UpdateCliAsync()
    {
        Busy = true;
        Message = "Downloading and verifying...";
        var release = await Service.Installer.FetchReleaseAsync();
        if (release is null)
        {
            Message = "The version page could not be read.";
            Busy = false;
            return;
        }
        var outcome = await Service.Installer.InstallAsync(release);
        Message = outcome.Message;
        Service.RefreshCliPath();
        Busy = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RollbackCliAsync()
    {
        Message = Service.Installer.Rollback().Message;
        Service.RefreshCliPath();
        await RefreshAsync();
    }

    /// A package should not land in your home folder, so cleanup happens here.
    [RelayCommand]
    private async Task CleanupAsync()
    {
        if (!ConfirmCleanup)
        {
            ConfirmCleanup = true;
            Message = "This deletes the database, the settings and the downloaded CLI. " +
                      "Whatever is already on Proton stays there. Press again to continue.";
            return;
        }

        Busy = true;
        var steps = await Cleanup.RunAsync();
        var failed = steps.Where(step => !step.Succeeded).ToList();
        Message = failed.Count == 0
            ? "Cleaned up. Remove the package with: sudo apt remove protonbackup"
            : "Partly cleaned up: " + string.Join("; ", failed.Select(step => step.Description));
        ConfirmCleanup = false;
        Busy = false;
    }

    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        IntervalMinutes = (int)SystemdManager.DefaultInterval.TotalMinutes;
        Service.CliSettings.RestoreDefaults();
        Message = "Defaults restored; apply the interval with Save interval.";
        await RefreshAsync();
    }
}
