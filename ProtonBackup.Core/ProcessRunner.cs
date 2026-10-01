using System.Diagnostics;
using System.Text;

namespace ProtonBackup.Core;

public static class ProcessRunner
{
    public static async Task<CliResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token = default)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            // A cancelled call must not leave the child (and its children) running.
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already gone */ }
            throw;
        }

        return new CliResult(process.ExitCode, await stdout, await stderr);
    }
}
