using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ProtonBackup.UI.Services;

namespace ProtonBackup.UI.Views;

public partial class MainWindow : Window, IFolderPicker
{
    public MainWindow()
    {
        InitializeComponent();
        LogIconPath.Data = NavIcons.Log();
        SettingsIconPath.Data = NavIcons.Gear();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}
