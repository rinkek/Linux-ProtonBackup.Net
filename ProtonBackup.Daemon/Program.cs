using System.Runtime.InteropServices;
using ProtonBackup.Core;

var command = args.Length > 0 ? args[0] : "--help";

using var cancellation = new CancellationTokenSource();
// systemctl --user stop sends SIGTERM; the run then stops cleanly and the next one continues.
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    cancellation.Cancel();
});
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

// Everything that touches the disk is created on demand: asking for help must not create any files.
Database? openDatabase = null;
Database database() => openDatabase ??= new Database();
void closeDatabase()
{
    var open = openDatabase;
    openDatabase = null;
    open?.Dispose();
}

var systemd = new SystemdManager();
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
CliInstaller installer() => new(database(), http, Console.WriteLine);
CliUpdateCheck updateCheck() => new(database(), installer(), Console.WriteLine);

try
{
    return await ExecuteAsync();
}
catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException
                                      or System.ComponentModel.Win32Exception or Microsoft.Data.Sqlite.SqliteException)
{
    Console.Error.WriteLine($"protonbackup: {exception.Message}");
    return 1;
}
finally
{
    closeDatabase();
}

async Task<int> ExecuteAsync()
{
    switch (command)
    {
        case "--run-once":
        {
            var cliPath = RequireCli();
            if (cliPath is null) return 2;

            long? sourceId = null;
            var index = Array.IndexOf(args, "--source");
            if (index >= 0 && index + 1 < args.Length)
            {
                if (!long.TryParse(args[index + 1].Trim(), out var parsedId))
                {
                    Console.Error.WriteLine("Usage: --run-once [--source ID] [--force]   (ID must be a number)");
                    return 2;
                }
                sourceId = parsedId;
            }

            if (args.Contains("--force"))
            {
                var reset = database().MarkAllPending(sourceId);
                Console.WriteLine($"Force: {reset} file(s) queued again.");
            }

            var engine = new SyncEngine(database(), new ProtonDriveCli(cliPath), Console.WriteLine);
            try
            {
                var result = await engine.RunOnceAsync(sourceId, cancellation.Token);
                // Only after the run, and a failed check must never change the outcome.
                try { await updateCheck().CheckAsync(token: cancellation.Token); }
                catch (Exception exception) { Console.Error.WriteLine($"Update check failed: {exception.Message}"); }
                return result.Failed > 0 ? 1 : 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }

        case "add-source":
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: add-source <local folder> <destination path on Proton>");
                return 2;
            }
            var id = database().AddSource(args[1], args[2]);
            Console.WriteLine($"Source {id}: {Path.GetFullPath(args[1])} -> {args[2]}");
            return 0;
        }

        case "remove-source":
            if (args.Length < 2) { Console.Error.WriteLine("Usage: remove-source <id>"); return 2; }
            if (!long.TryParse(args[1].Trim(), out var removeId)) { Console.Error.WriteLine("Usage: remove-source <id>"); return 2; }
            database().RemoveSource(removeId);
            return 0;

        case "list-sources":
            foreach (var source in database().GetSources())
                Console.WriteLine($"{source.Id}\t{(source.Enabled ? "on" : "off")}\t{source.LocalPath} -> {source.RemotePath}");
            return 0;

        case "install-units":
            await systemd.InstallUnitsAsync(ReadInterval(), cancellation.Token);
            Console.WriteLine($"Units written for {Environment.ProcessPath}");
            Console.WriteLine("Turn on with: protonbackup enable-timer");
            return 0;

        case "enable-timer":
            return await ReportAsync(systemd.EnableTimerAsync(cancellation.Token), "Timer is on.");

        case "disable-timer":
            return await ReportAsync(systemd.DisableTimerAsync(cancellation.Token), "Timer is off.");

        case "set-interval":
        {
            var interval = ReadInterval();
            if (interval is null)
            {
                Console.Error.WriteLine("Usage: set-interval <minutes>");
                return 2;
            }
            await systemd.SetIntervalAsync(interval.Value, cancellation.Token);
            database().SetSetting("interval_minutes", ((long)interval.Value.TotalMinutes).ToString());
            Console.WriteLine($"Interval set to {(long)interval.Value.TotalMinutes} minutes.");
            return 0;
        }

        case "start":
        {
            long? sourceId = args.Length > 1 && long.TryParse(args[1], out var parsed) ? parsed : null;
            if (await systemd.IsSyncRunningAsync(cancellation.Token))
            {
                Console.WriteLine("A run is already in progress.");
                return 0;
            }
            return await ReportAsync(systemd.StartSyncAsync(sourceId, cancellation.Token), "Run started.");
        }

        case "stop":
            return await ReportAsync(systemd.StopSyncAsync(cancellation.Token), "Run stopped; the next one continues where this one left off.");

        case "force-all":
        {
            long? sourceId = args.Length > 1 && long.TryParse(args[1], out var parsed) ? parsed : null;
            var reset = database().MarkAllPending(sourceId);
            Console.WriteLine($"{reset} file(s) queued again.");
            Console.WriteLine("Run a sync with: protonbackup --run-once");
            return 0;
        }

        case "check-update":
        {
            var status = await updateCheck().CheckAsync(force: true, cancellation.Token);
            if (status is null)
            {
                Console.WriteLine("The version page could not be read.");
                return 1;
            }
            Console.WriteLine($"Installed: {status.Installed ?? "none"}");
            Console.WriteLine($"Available: {status.Available}");
            Console.WriteLine(status.UpdateAvailable ? "A newer version is available." : "You are up to date.");
            return 0;
        }

        case "update-cli":
        {
            var release = await installer().FetchReleaseAsync(cancellation.Token);
            if (release is null)
            {
                Console.Error.WriteLine("The version page could not be read.");
                return 1;
            }
            var force = args.Contains("--allow-missing-checksum");
            var outcome = await installer().InstallAsync(release, force, cancellation.Token);
            Console.WriteLine(outcome.Message);
            return outcome.Success ? 0 : 1;
        }

        case "rollback-cli":
        {
            var outcome = installer().Rollback();
            Console.WriteLine(outcome.Message);
            return outcome.Success ? 0 : 1;
        }

        case "--cleanup":
        {
            Console.WriteLine("This deletes the database, the settings, the downloaded CLI and the systemd units.");
            Console.WriteLine("Whatever is already on Proton Drive stays there.");
            if (!args.Contains("--yes"))
            {
                Console.Write("Continue? [y/N] ");
                var answer = Console.ReadLine();
                if (answer?.Trim().ToLowerInvariant() is not ("y" or "yes"))
                {
                    Console.WriteLine("Cancelled.");
                    return 1;
                }
            }

            var steps = await Cleanup.RunAsync(closeDatabase, cancellation.Token);
            foreach (var step in steps)
                Console.WriteLine($"  {(step.Succeeded ? "ok " : "failed")}  {step.Description}" +
                                  (step.Detail is null ? "" : $" ({step.Detail})"));
            Console.WriteLine("Done. Remove the package with: sudo apt remove protonbackup");
            return Cleanup.Succeeded(steps) ? 0 : 1;
        }

        case "status":
        {
            var cliPath = ProtonDriveCli.Locate(database().GetSetting("cli_path"));
            Console.WriteLine($"CLI:      {cliPath ?? "not found"}");
            if (cliPath is not null)
            {
                var cli = new ProtonDriveCli(cliPath);
                Console.WriteLine($"Version:  {await cli.GetVersionAsync(cancellation.Token)}");
                Console.WriteLine($"Session:  {await cli.CheckSessionAsync(cancellation.Token)}");
            }
            Console.WriteLine($"Database: {AppPaths.DatabasePath}");
            Console.WriteLine($"Timer:    {(await systemd.IsTimerEnabledAsync(cancellation.Token) ? "on" : "off")}");
            Console.WriteLine($"Run:      {(await systemd.IsSyncRunningAsync(cancellation.Token) ? "in progress" : "not active")}");
            foreach (var source in database().GetSources())
                Console.WriteLine($"Source {source.Id}: {source.LocalPath} -> {source.RemotePath} ({(source.Enabled ? "on" : "off")})");

            var timers = await systemd.DescribeTimerAsync(cancellation.Token);
            if (!string.IsNullOrWhiteSpace(timers)) Console.WriteLine($"\n{timers}");
            return 0;
        }

        default:
            Console.WriteLine("""
                protonbackup
                  --run-once [--source ID] [--force]
                                             Runs a sync; --force ignores what the
                                             database already considers synced
                  add-source <folder> <destination>
                                             Adds a source folder
                  remove-source <id>         Removes a source folder
                  list-sources               Lists the source folders
                  force-all [source-id]      Queues everything again
                  install-units [minutes]    Writes the systemd user units
                  enable-timer               Turns automatic syncing on
                  disable-timer              Turns automatic syncing off
                  set-interval <minutes>     Changes the interval via a drop-in
                  start [source-id]          Starts a run via systemd
                  stop                       Stops the run in progress
                  status                     Shows CLI, session, timer and sources
                  check-update               Checks whether a newer CLI is available
                  update-cli                 Downloads and installs the latest CLI
                  rollback-cli               Restores the previous CLI
                  --cleanup [--yes]          Removes database, settings, CLI and units
                """);
            return 0;
    }
}

string? RequireCli()
{
    var path = ProtonDriveCli.Locate(database().GetSetting("cli_path"));
    if (path is null)
        Console.Error.WriteLine($"The proton-drive CLI was not found. Put it in {AppPaths.BinDir} or on PATH.");
    return path;
}

TimeSpan? ReadInterval() =>
    args.Length > 1 && int.TryParse(args[1], out var minutes) && minutes > 0
        ? TimeSpan.FromMinutes(minutes)
        : null;

async Task<int> ReportAsync(Task<CliResult> action, string success)
{
    var result = await action;
    Console.WriteLine(result.Ok ? success : result.Output.Trim());
    return result.Ok ? 0 : 1;
}
