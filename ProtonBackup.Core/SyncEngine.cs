namespace ProtonBackup.Core;

public sealed class SyncEngine(Database database, ProtonDriveCli cli, Action<string>? log = null)
{
    /// Batching per target folder is ~23x faster than per file (phase 1, test 3).
    /// The limit keeps the argument list well below ARG_MAX and progress visible.
    private const int BatchSize = 200;

    private readonly Scanner _scanner = new();

    private void Log(string message) => log?.Invoke(message);

    public async Task<SyncResult> RunOnceAsync(long? onlySourceId = null, CancellationToken token = default)
    {
        using var syncLock = SyncLock.TryAcquire();
        if (syncLock is null)
        {
            Log("A run is already in progress; this one is skipped.");
            return SyncResult.Blocked("already-running");
        }

        var session = await cli.CheckSessionAsync(token);
        if (session != SessionState.Active)
        {
            Log(session == SessionState.Expired
                ? "Session expired. Sign in again with: proton-drive auth login"
                : "Cannot check the session; the run is paused.");
            return SyncResult.Blocked(session == SessionState.Expired ? "session-expired" : "session-unknown");
        }

        var runId = database.StartRun();
        var progress = new RunProgress();

        try
        {
            foreach (var source in database.GetSources(onlyEnabled: true))
            {
                if (onlySourceId is not null && source.Id != onlySourceId) continue;
                token.ThrowIfCancellationRequested();
                await SyncSourceAsync(source, progress, token);
            }

            var outcome = progress.Failed == 0 ? "ok" : "partial";
            database.FinishRun(runId, progress.Uploaded, progress.Failed, outcome);
            Log($"Done: {progress.Uploaded} uploaded, {progress.Failed} failed, {progress.Missing} gone locally.");
            return new SyncResult(progress.Uploaded, progress.Failed, progress.Missing, outcome);
        }
        catch (OperationCanceledException)
        {
            // The counters advance per batch, so an aborted run is not logged as zero.
            database.FinishRun(runId, progress.Uploaded, progress.Failed, "cancelled");
            Log($"Cancelled after {progress.Uploaded} file(s); the next run continues where this one left off.");
            throw;
        }
        catch (Exception exception)
        {
            database.FinishRun(runId, progress.Uploaded, progress.Failed, "error");
            Log($"Run failed: {exception.Message}");
            throw;
        }
    }

    private async Task SyncSourceAsync(SyncSource source, RunProgress progress, CancellationToken token)
    {
        Log($"Source {source.LocalPath} -> {source.RemotePath}");

        var scanned = _scanner.Scan(source.LocalPath).ToList();
        var tracked = database.GetTrackedFiles(source.Id);

        var changed = scanned.Where(file => Scanner.NeedsUpload(file, tracked.GetValueOrDefault(file.RelativePath))).ToList();
        if (changed.Count > 0) database.UpsertPending(source.Id, changed);

        var missing = database.MarkMissing(source.Id, scanned.Select(file => file.RelativePath).ToList());
        progress.Missing += missing;
        if (missing > 0) Log($"  {missing} file(s) gone locally; they stay on Proton.");

        var pending = database.GetPending(source.Id);
        if (pending.Count == 0)
        {
            Log("  Nothing to do.");
            return;
        }

        Log($"  {pending.Count} file(s) to upload.");

        foreach (var group in pending.GroupBy(file => DirectoryOf(file.RelativePath)))
        {
            token.ThrowIfCancellationRequested();

            var remoteParent = group.Key.Length == 0
                ? source.RemotePath
                : RemotePath.Combine(source.RemotePath, group.Key);

            await cli.EnsureFolderAsync(remoteParent, CancellationToken.None);

            foreach (var batch in group.Chunk(BatchSize))
            {
                token.ThrowIfCancellationRequested();

                var localPaths = batch.Select(file => Path.Combine(source.LocalPath, file.RelativePath)).ToList();
                // A batch that has started is finished and verified; cancellation is only honoured at the
                // checkpoints between batches, so the counters stay true after a stop.
                var result = await cli.UploadAsync(localPaths, remoteParent, CancellationToken.None);

                var confirmed = await ConfirmUploadedAsync(remoteParent, batch, token);
                if (confirmed is null)
                {
                    Log($"  {remoteParent}: could not fetch the folder listing; this batch is left for the next run.");
                    continue;
                }

                var succeeded = batch.Where(file => confirmed.Contains(Path.GetFileName(file.RelativePath))).ToList();
                var rejected = batch.Except(succeeded).ToList();

                if (succeeded.Count > 0)
                    database.MarkSynced(source.Id, succeeded.Select(file => file.RelativePath));

                if (rejected.Count > 0)
                {
                    var message = FailureMessage(result);
                    database.MarkError(source.Id, rejected.Select(file => file.RelativePath), message);
                }

                progress.Uploaded += succeeded.Count;
                progress.Failed += rejected.Count;
                Log($"  {remoteParent}: {succeeded.Count} ok, {rejected.Count} failed");
            }
        }
    }

    /// Why a file was not on Proton afterwards. A CLI that exits 0 yet stored nothing gets its own message.
    private static string FailureMessage(CliResult result)
    {
        if (result.Ok) return "Not found on Proton after uploading.";
        var output = result.Output.Trim();
        return output.Length == 0 ? $"Upload failed with exit code {result.ExitCode}." : output;
    }

    private sealed class RunProgress
    {
        public int Uploaded;
        public int Failed;
        public int Missing;
    }

    /// The exit code alone is no proof: without a strategy flag the CLI silently skips files
    /// with exit code 0 (phase 1, test 1d). That is why every batch is checked against the folder listing.
    private async Task<HashSet<string>?> ConfirmUploadedAsync(string remoteParent, IEnumerable<TrackedFile> batch, CancellationToken token)
    {
        var expected = batch.ToDictionary(file => Path.GetFileName(file.RelativePath), file => file.Size, StringComparer.Ordinal);

        var nodes = await cli.ListAsync(remoteParent, CancellationToken.None);
        if (nodes is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            nodes = await cli.ListAsync(remoteParent, CancellationToken.None);
        }
        if (nodes is null) return null;

        var confirmed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node.IsFolder || node.FileName is not { } name) continue;
            if (!expected.TryGetValue(name, out var size)) continue;
            if (node.ActiveRevision?.ClaimedSize is { } claimed && claimed != size) continue;
            confirmed.Add(name);
        }
        return confirmed;
    }

    private static string DirectoryOf(string relativePath)
    {
        var directory = Path.GetDirectoryName(relativePath) ?? "";
        return directory.Replace(Path.DirectorySeparatorChar, '/');
    }
}
