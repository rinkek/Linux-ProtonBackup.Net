using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using ProtonBackup.UI.Services;
using ProtonBackup.UI.ViewModels;
using ProtonBackup.UI.Views;

namespace ProtonBackup.UI;

public partial class App : Application
{
    private BackupService? _service;
    private MainWindowViewModel? _viewModel;
    private TrayIcon? _trayIcon;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _service = new BackupService();
            var window = new MainWindow();
            _viewModel = new MainWindowViewModel(_service, window);
            window.DataContext = _viewModel;
            desktop.MainWindow = window;

            SetUpTrayIcon(desktop, window);
            desktop.ShutdownRequested += (_, _) =>
            {
                _viewModel.Dispose();
                _service.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetUpTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, Window window)
    {
        var show = new NativeMenuItem("Openen");
        show.Click += (_, _) =>
        {
            window.Show();
            window.Activate();
        };
        var quit = new NativeMenuItem("Afsluiten");
        quit.Click += (_, _) => desktop.Shutdown();

        _trayIcon = new TrayIcon
        {
            Icon = LoadIcon("ok"),
            ToolTipText = "Proton Drive backup",
            Menu = [show, quit],
        };
        _trayIcon.Clicked += (_, _) =>
        {
            window.Show();
            window.Activate();
        };
        TrayIcon.SetIcons(this, [_trayIcon]);

        _viewModel!.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainWindowViewModel.TrayState)) return;
            var state = _viewModel.TrayState;
            _trayIcon.Icon = LoadIcon(state);
            _trayIcon.ToolTipText = state switch
            {
                "bezig" => "Proton Drive backup — bezig met synchroniseren",
                "fout" => "Proton Drive backup — er zijn bestanden met een fout",
                _ => "Proton Drive backup — bijgewerkt",
            };
        };
    }

    private static WindowIcon LoadIcon(string state)
    {
        var name = state switch { "bezig" => "tray-busy", "fout" => "tray-error", _ => "tray-ok" };
        // The avares URI uses the assembly name, not the project name.
        var assembly = typeof(App).Assembly.GetName().Name;
        using var stream = AssetLoader.Open(new Uri($"avares://{assembly}/Assets/{name}.png"));
        return new WindowIcon(new Bitmap(stream));
    }
}
