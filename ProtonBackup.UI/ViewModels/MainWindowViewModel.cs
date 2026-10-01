using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtonBackup.Core;
using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.ViewModels;

/// What the tray icon shows; derived from the Status page, so the icon cannot disagree with the window.
public enum TrayState { Ok, Busy, Error }

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly BackupService _service;
    private readonly DispatcherTimer _timer;
    private int _ticks;
    private bool _ticking;

    public MainWindowViewModel(BackupService service, IFolderPicker folderPicker)
    {
        _service = service;
        Status = new StatusPageViewModel(service);
        Structure = new StructurePageViewModel(service);
        SourceFolders = new SourceFoldersPageViewModel(service, folderPicker);
        Log = new LogPageViewModel(service);
        Settings = new SettingsPageViewModel(service);
        Welcome = new WelcomePageViewModel(service, folderPicker);
        Welcome.Finished += (_, _) =>
        {
            ShowWelcome = false;
            _ = RefreshSlowAsync();
        };
        Settings.AccountChanged += (_, _) => _ = RefreshSlowAsync();
        Settings.RemovalStarted += (_, _) =>
        {
            // The database is about to be deleted; nothing may poll it again.
            Removing = true;
            _timer?.Stop();
        };
        Settings.RemovalFinished += (_, steps) => RemovalSteps = steps;
        _showWelcome = WelcomePageViewModel.IsNeeded(service);
        if (_showWelcome) _ = Welcome.RefreshAsync().ContinueWith(_ => Welcome.RefreshSlowAsync(), TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();

        // Log and Settings sit in their own icon row at the bottom of the sidebar
        // (see MainWindow.axaml), not in this list — a deliberate change from the
        // one-list-of-five layout, requested by the user for both apps.
        Pages = [Status, SourceFolders, Structure];
        _selectedPage = Status;

        // The database is the only link with the daemon, so the UI polls it periodically.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
        _ = TickAsync();
        _ = RefreshSlowAsync();
    }

    public StatusPageViewModel Status { get; }
    public StructurePageViewModel Structure { get; }
    public SourceFoldersPageViewModel SourceFolders { get; }
    public LogPageViewModel Log { get; }
    public SettingsPageViewModel Settings { get; }

    public WelcomePageViewModel Welcome { get; }

    public ObservableCollection<PageViewModelBase> Pages { get; }

    [ObservableProperty] private PageViewModelBase _selectedPage;

    [ObservableProperty] private TrayState _trayState = TrayState.Ok;
    [ObservableProperty] private bool _cliMissing;
    [ObservableProperty] private bool _showWelcome;
    [ObservableProperty] private bool _removing;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RemovalFinished))] [NotifyPropertyChangedFor(nameof(RemovalText))]
    private IReadOnlyList<CleanupStep>? _removalSteps;

    public bool NotRemoving => !Removing;
    public bool RemovalFinished => RemovalSteps is not null;

    /// One step per line, failed ones marked.
    public string RemovalText => RemovalSteps is null
        ? "Removing everything the app put in your home folder..."
        : string.Join("\n", RemovalSteps.Select(step =>
              $"{(step.Succeeded ? "✓" : "✗")}  {step.Description}" + (step.Detail is null || step.Succeeded ? "" : $" ({step.Detail})")))
          + (Cleanup.Succeeded(RemovalSteps)
              ? "\n\nAll done. Remove the package with: sudo apt remove protonbackup"
              : "\n\nSome steps failed; see the list above.");

    partial void OnRemovingChanged(bool value) => OnPropertyChanged(nameof(NotRemoving));

    public bool IsLogSelected => SelectedPage == Log;
    public bool IsSettingsSelected => SelectedPage == Settings;

    /// The nav ListBox's own SelectedItem, bound two-way. Whenever SelectedPage
    /// is Settings or Log — not in Pages, since those moved to their own icon row
    /// — Avalonia can't find a matching item and resets the ListBox's selection to
    /// null; because SelectedItem was bound directly to SelectedPage before this
    /// existed, that reset wrote straight back into SelectedPage too, crashing the
    /// next tick's `await SelectedPage.RefreshAsync()` with a NullReferenceException
    /// (found by actually navigating to Settings). This property absorbs that
    /// null write-back instead of forwarding it.
    public PageViewModelBase? SelectedNavItem
    {
        get => Pages.Contains(SelectedPage) ? SelectedPage : null;
        set
        {
            if (value is not null) SelectedPage = value;
        }
    }

    [RelayCommand]
    private void SelectPage(PageViewModelBase? page)
    {
        if (page is not null) SelectedPage = page;
    }

    private async Task TickAsync()
    {
        // One load at a time: a slow probe must not pile up behind the timer.
        if (Removing || _ticking) return;
        _ticking = true;
        try
        {
            if (ShowWelcome)
            {
                await Welcome.RefreshAsync();
                if (++_ticks % 15 == 0) await Welcome.RefreshSlowAsync();
                return;
            }

            await Status.RefreshAsync();
            if (SelectedPage != Status) await SelectedPage.RefreshAsync();

            CliMissing = _service.CliPath is null;
            TrayState = Status.IsRunning ? TrayState.Busy : Status.ErrorCount > 0 ? TrayState.Error : TrayState.Ok;

            // The session and CLI version cost a subprocess, so not on every tick.
            if (++_ticks % 15 == 0) await RefreshSlowAsync();
        }
        finally
        {
            _ticking = false;
        }
    }

    private async Task RefreshSlowAsync()
    {
        if (Removing) return;
        if (ShowWelcome) await Welcome.RefreshSlowAsync();
        await Status.RefreshSlowAsync();
        await Settings.RefreshSessionAsync();
        // Daily; a failed check is ignored and blocks nothing.
        try { await Status.CheckForUpdateAsync(); } catch (HttpRequestException) { }
    }

    partial void OnSelectedPageChanged(PageViewModelBase value)
    {
        _ = value.RefreshAsync();
        OnPropertyChanged(nameof(IsLogSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(SelectedNavItem));
    }

    public void Dispose() => _timer.Stop();
}
