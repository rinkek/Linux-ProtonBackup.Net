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
    /// True while the CLI reports a working session: signing in again would do nothing, so the button is disabled.
    [ObservableProperty] private bool _signedIn;

    private bool _formLoaded;

    /// Raised when the removal starts (polling must stop for good) and with the steps when it is over.
    public event EventHandler? RemovalStarted;
    public event EventHandler<IReadOnlyList<CleanupStep>>? RemovalFinished;
    /// Raised after a sign-in, sign-out or CLI install, so the rest of the window re-reads the session at once.
    public event EventHandler? AccountChanged;

    /// The form is loaded once when the page is first shown, and again after something was saved, not on every
    /// two-second tick: reloading it each tick threw away whatever the user was typing.
    public override Task RefreshAsync()
    {
        CliPath = Service.CliPath ?? "";
        if (!_formLoaded) LoadForm(includingInterval: true);
        return Task.CompletedTask;
    }

    private void LoadForm(bool includingInterval)
    {
        if (includingInterval && int.TryParse(Service.Database.GetSetting("interval_minutes"), out var minutes) && minutes >= 1)
            IntervalMinutes = minutes;
        VersionPageUrl = Service.CliSettings.VersionPageUrl;
        DownloadTemplate = Service.CliSettings.DownloadTemplate;
        Platform = Service.CliSettings.Platform;
        SkipChecksum = Service.CliSettings.SkipChecksum;
        _formLoaded = true;
    }

    public async Task RefreshSessionAsync()
    {
        var state = await Service.GetSessionAsync();
        SignedIn = state == SessionState.Active;
        SessionText = state switch
        {
            SessionState.Active => "signed in",
            SessionState.Expired => "session expired",
            _ => "unknown",
        };
    }

    /// Sign in is the very first thing shown on this page, with no earlier point
    /// where a first-time user would have had a reason to visit the CLI's own
    /// Update button - so a missing CLI is fetched here instead of just reporting
    /// "CLI not found", found confusing by actually using the app.
    /// Sign in is the very first thing shown on this page, so a missing CLI is fetched here instead of just
    /// reporting "CLI not found": one click from a fresh install.
    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task LoginAsync()
    {
        Busy = true;
        var outcome = await Service.SignInAsync(text => Message = text);
        Message = outcome.Message;
        CliPath = Service.CliPath ?? "";
        Busy = false;
        await RefreshSessionAsync();
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSignIn() => !SignedIn && !Busy;

    partial void OnSignedInChanged(bool value) => LoginCommand.NotifyCanExecuteChanged();
    partial void OnBusyChanged(bool value) => LoginCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task LogoutAsync()
    {
        Busy = true;
        var result = await Service.LogoutAsync();
        Message = result.Ok ? "Signed out." : result.Output.Trim();
        Busy = false;
        await RefreshSessionAsync();
        AccountChanged?.Invoke(this, EventArgs.Empty);
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
        LoadForm(includingInterval: false);
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
        CliPath = Service.CliPath ?? "";
        Busy = false;
        LoadForm(includingInterval: false);
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RollbackCliAsync()
    {
        Message = Service.Installer.Rollback().Message;
        Service.RefreshCliPath();
        CliPath = Service.CliPath ?? "";
        AccountChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
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

        ConfirmCleanup = false;
        Busy = true;
        RemovalStarted?.Invoke(this, EventArgs.Empty);
        var steps = await Service.RemoveEverythingAsync();
        Busy = false;
        RemovalFinished?.Invoke(this, steps);
    }

    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        IntervalMinutes = (int)SystemdManager.DefaultInterval.TotalMinutes;
        Service.CliSettings.RestoreDefaults();
        Message = "Defaults restored; apply the interval with Save interval.";
        // The interval just set to the default is not stored yet and must stay.
        LoadForm(includingInterval: false);
        await Task.CompletedTask;
    }
}
