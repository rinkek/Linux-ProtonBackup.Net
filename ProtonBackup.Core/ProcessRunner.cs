using System.Diagnostics;

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
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);

        return new CliResult(process.ExitCode, await stdout, await stderr);
    }
}
