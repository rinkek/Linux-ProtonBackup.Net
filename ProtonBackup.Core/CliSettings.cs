namespace ProtonBackup.Core;

/// Settings for fetching and updating the CLI, with a "restore defaults" button
/// in the UI as a safety net for when the page changes location or shape.
public sealed class CliSettings(Database database)
{
    public const string DefaultVersionPageUrl = "https://proton.me/download/drive/cli/index.html";
    public const string DefaultDownloadTemplate = "https://proton.me/download/drive/cli/{version}/{platform}/proton-drive";
    public const string DefaultPlatform = "linux-x64";
    public const string BaselinePlatform = "linux-x64-baseline";

    public string VersionPageUrl
    {
        get => database.GetSetting("cli_version_page_url") ?? DefaultVersionPageUrl;
        set => database.SetSetting("cli_version_page_url", value);
    }

    public string DownloadTemplate
    {
        get => database.GetSetting("cli_download_template") ?? DefaultDownloadTemplate;
        set => database.SetSetting("cli_download_template", value);
    }

    public string Platform
    {
        get => database.GetSetting("cli_platform") ?? DefaultPlatform;
        set => database.SetSetting("cli_platform", value);
    }

    public bool SkipChecksum
    {
        get => database.GetSetting("cli_skip_checksum") == "1";
        set => database.SetSetting("cli_skip_checksum", value ? "1" : "0");
    }

    public string? LastSeenVersion
    {
        get => database.GetSetting("cli_last_seen_version");
        set => database.SetSetting("cli_last_seen_version", value ?? "");
    }

    /// Remembers which version was dismissed, so "Later" only comes back for an even newer one.
    public string? DismissedVersion
    {
        get => database.GetSetting("cli_dismissed_version");
        set => database.SetSetting("cli_dismissed_version", value ?? "");
    }

    public void RestoreDefaults()
    {
        VersionPageUrl = DefaultVersionPageUrl;
        DownloadTemplate = DefaultDownloadTemplate;
        Platform = DefaultPlatform;
        SkipChecksum = false;
    }

    public string BuildDownloadUrl(string version, string platform) =>
        DownloadTemplate.Replace("{version}", version).Replace("{platform}", platform);
}
