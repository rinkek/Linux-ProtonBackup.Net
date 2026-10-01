using ProtonBackup.Core;

namespace ProtonBackup.UI.ViewModels;

/// Shows a sync run in local time and with a Dutch outcome.
public sealed class RunRowViewModel(RunRecord record)
{
    public long Id => record.Id;
    public string Started => record.StartedUtc.ToLocalTime().ToString("dd-MM HH:mm");
    public int Uploaded => record.Uploaded;
    public int Failed => record.Failed;
    public string Outcome => Describe(record.Result);

    public static string Describe(string? result) => result switch
    {
        "ok" => "completed",
        "partial" => "partly failed",
        "cancelled" => "cancelled",
        "error" => "error",
        "already-running" => "skipped",
        "session-expired" => "session expired",
        "session-unknown" => "session unknown",
        null => "running",
        _ => result,
    };
}
