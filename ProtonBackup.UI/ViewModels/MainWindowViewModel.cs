using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly BackupService _service;
    private readonly DispatcherTimer _timer;
    private int _ticks;

    public MainWindowViewModel(BackupService service, IFolderPicker folderPicker)
    {
        _service = service;
        Status = new StatusPageViewModel(service);
        Structure = new StructurePageViewModel(service);
        SourceFolders = new SourceFoldersPageViewModel(service, folderPicker);
        Log = new LogPageViewModel(service);
        Settings = new SettingsPageViewModel(service);
        Welcome = new WelcomePageViewModel(service, folderPicker);
        Welcome.Finished += (_, _) => ShowWelcome = false;
        _showWelcome = WelcomePageViewModel.IsNeeded(service);
        if (_showWelcome) _ = Welcome.RefreshAsync();

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

    [ObservableProperty] private string _trayState = "ok";
    [ObservableProperty] private bool _cliMissing;
    [ObservableProperty] private bool _showWelcome;

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
        if (ShowWelcome)
        {
            await Welcome.RefreshAsync();
            return;
        }

        await Status.RefreshAsync();
        await SelectedPage.RefreshAsync();

        CliMissing = _service.CliPath is null;
        TrayState = Status.IsRunning ? "running" : Status.ErrorCount > 0 ? "error" : "ok";

        // The session and CLI version cost a subprocess, so not on every tick.
        if (++_ticks % 15 == 0) await RefreshSlowAsync();
    }

    private async Task RefreshSlowAsync()
    {
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
