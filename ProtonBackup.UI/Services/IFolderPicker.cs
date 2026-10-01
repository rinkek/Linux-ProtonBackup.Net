namespace ProtonBackup.UI.Services;

public interface IFolderPicker
{
    Task<string?> PickFolderAsync(string title);
}
