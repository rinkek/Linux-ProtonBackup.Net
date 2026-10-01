namespace ProtonBackup.Core;

public sealed record CliResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
    public string Output => string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdOut + StdErr;

    /// The CLI reports a missing session as plain text, not as JSON.
    public bool NotLoggedIn => Output.Contains("You need to login first", StringComparison.OrdinalIgnoreCase);

    public bool AlreadyExists => Output.Contains("already exists", StringComparison.OrdinalIgnoreCase);
    public bool NotFound => Output.Contains("Node not found", StringComparison.OrdinalIgnoreCase);
}
