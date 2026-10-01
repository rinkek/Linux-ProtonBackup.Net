using Microsoft.Data.Sqlite;

namespace ProtonBackup.Core;

public sealed class Database : IDisposable
{
    private readonly SqliteConnection _connection;

    public Database(string? path = null)
    {
        AppPaths.EnsureCreated();
        _connection = new SqliteConnection($"Data Source={path ?? AppPaths.DatabasePath}");
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;");
        CreateSchema();
    }

    private void CreateSchema() => Execute("""
        CREATE TABLE IF NOT EXISTS sources (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            local_path  TEXT NOT NULL UNIQUE,
            remote_path TEXT NOT NULL,
            enabled     INTEGER NOT NULL DEFAULT 1
        );
        CREATE TABLE IF NOT EXISTS files (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            source_id      INTEGER NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
            rel_path       TEXT NOT NULL,
            size           INTEGER NOT NULL,
            mtime_unix_ms  INTEGER NOT NULL,
            inode          INTEGER,
            last_sync_utc  TEXT,
            status         TEXT NOT NULL,
            last_error     TEXT,
            UNIQUE(source_id, rel_path)
        );
        CREATE INDEX IF NOT EXISTS idx_files_status ON files(source_id, status);
        CREATE TABLE IF NOT EXISTS runs (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            started_utc   TEXT NOT NULL,
            finished_utc  TEXT,
            uploaded      INTEGER NOT NULL DEFAULT 0,
            failed        INTEGER NOT NULL DEFAULT 0,
            result        TEXT
        );
        CREATE TABLE IF NOT EXISTS settings (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """);

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    // ---- sources ----------------------------------------------------------

    public long AddSource(string localPath, string remotePath)
    {
        using var command = Command(
            "INSERT INTO sources (local_path, remote_path, enabled) VALUES ($l, $r, 1) " +
            "ON CONFLICT(local_path) DO UPDATE SET remote_path = $r RETURNING id;");
        command.Parameters.AddWithValue("$l", NormalisePath(localPath));
        command.Parameters.AddWithValue("$r", remotePath);
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<SyncSource> GetSources(bool onlyEnabled = false)
    {
        using var command = Command(
            "SELECT id, local_path, remote_path, enabled FROM sources" +
            (onlyEnabled ? " WHERE enabled = 1" : "") + " ORDER BY id;");
        using var reader = command.ExecuteReader();
        var result = new List<SyncSource>();
        while (reader.Read())
            result.Add(new SyncSource(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0));
        return result;
    }

    public void SetSourceEnabled(long sourceId, bool enabled)
    {
        using var command = Command("UPDATE sources SET enabled = $e WHERE id = $id;");
        command.Parameters.AddWithValue("$e", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$id", sourceId);
        command.ExecuteNonQuery();
    }

    public void RemoveSource(long sourceId)
    {
        using var command = Command("DELETE FROM sources WHERE id = $id;");
        command.Parameters.AddWithValue("$id", sourceId);
        command.ExecuteNonQuery();
    }

    // ---- files ------------------------------------------------------------

    public Dictionary<string, TrackedFile> GetTrackedFiles(long sourceId)
    {
        using var command = Command("SELECT rel_path, size, mtime_unix_ms, status FROM files WHERE source_id = $id;");
        command.Parameters.AddWithValue("$id", sourceId);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, TrackedFile>(StringComparer.Ordinal);
        while (reader.Read())
            result[reader.GetString(0)] = new TrackedFile(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3));
        return result;
    }

    public void UpsertPending(long sourceId, IEnumerable<ScannedFile> files)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = Command("""
            INSERT INTO files (source_id, rel_path, size, mtime_unix_ms, inode, status, last_error)
            VALUES ($s, $p, $size, $m, $i, 'pending', NULL)
            ON CONFLICT(source_id, rel_path) DO UPDATE SET
                size = $size, mtime_unix_ms = $m, inode = $i, status = 'pending', last_error = NULL;
            """);
        command.Transaction = transaction;
        var p = command.Parameters;
        p.AddWithValue("$s", sourceId);
        p.Add("$p", SqliteType.Text);
        p.Add("$size", SqliteType.Integer);
        p.Add("$m", SqliteType.Integer);
        p.Add("$i", SqliteType.Integer);

        foreach (var file in files)
        {
            p["$p"].Value = file.RelativePath;
            p["$size"].Value = file.Size;
            p["$m"].Value = file.ModifiedUnixMs;
            p["$i"].Value = (object?)file.Inode ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// Sets everything back to pending, for example when something on Proton was thrown away that
    /// the database knows nothing about. Files that disappeared locally are left alone: there is nothing
    /// to upload for them. During the run itself the CLI skips whatever already matches by content.
    public int MarkAllPending(long? sourceId = null)
    {
        using var command = Command(
            "UPDATE files SET status = 'pending', last_error = NULL " +
            "WHERE status <> 'missing'" + (sourceId is null ? "" : " AND source_id = $s") + ";");
        if (sourceId is not null) command.Parameters.AddWithValue("$s", sourceId);
        return command.ExecuteNonQuery();
    }

    public IReadOnlyList<TrackedFile> GetPending(long sourceId)
    {
        using var command = Command(
            "SELECT rel_path, size, mtime_unix_ms, status FROM files " +
            "WHERE source_id = $id AND status IN ('pending','error') ORDER BY rel_path;");
        command.Parameters.AddWithValue("$id", sourceId);
        using var reader = command.ExecuteReader();
        var result = new List<TrackedFile>();
        while (reader.Read())
            result.Add(new TrackedFile(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3)));
        return result;
    }

    public void MarkSynced(long sourceId, IEnumerable<string> relativePaths)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = Command(
            "UPDATE files SET status = 'synced', last_sync_utc = $now, last_error = NULL " +
            "WHERE source_id = $s AND rel_path = $p;");
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$s", sourceId);
        command.Parameters.Add("$p", SqliteType.Text);
        foreach (var path in relativePaths)
        {
            command.Parameters["$p"].Value = path;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void MarkError(long sourceId, IEnumerable<string> relativePaths, string message)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = Command(
            "UPDATE files SET status = 'error', last_error = $e WHERE source_id = $s AND rel_path = $p;");
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$e", message);
        command.Parameters.AddWithValue("$s", sourceId);
        command.Parameters.Add("$p", SqliteType.Text);
        foreach (var path in relativePaths)
        {
            command.Parameters["$p"].Value = path;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// Files that disappeared locally are marked, but never touched on Proton.
    public int MarkMissing(long sourceId, IReadOnlyCollection<string> presentPaths)
    {
        using var transaction = _connection.BeginTransaction();
        Execute("CREATE TEMP TABLE IF NOT EXISTS present (rel_path TEXT PRIMARY KEY);");
        Execute("DELETE FROM present;");

        using (var insert = Command("INSERT OR IGNORE INTO present (rel_path) VALUES ($p);"))
        {
            insert.Transaction = transaction;
            insert.Parameters.Add("$p", SqliteType.Text);
            foreach (var path in presentPaths)
            {
                insert.Parameters["$p"].Value = path;
                insert.ExecuteNonQuery();
            }
        }

        using var update = Command(
            "UPDATE files SET status = 'missing' WHERE source_id = $s AND status <> 'missing' " +
            "AND rel_path NOT IN (SELECT rel_path FROM present);");
        update.Transaction = transaction;
        update.Parameters.AddWithValue("$s", sourceId);
        var affected = update.ExecuteNonQuery();
        transaction.Commit();
        return affected;
    }

    // ---- runs -------------------------------------------------------------

    public long StartRun()
    {
        using var command = Command("INSERT INTO runs (started_utc) VALUES ($now) RETURNING id;");
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        return (long)command.ExecuteScalar()!;
    }

    public void FinishRun(long runId, int uploaded, int failed, string result)
    {
        using var command = Command(
            "UPDATE runs SET finished_utc = $now, uploaded = $u, failed = $f, result = $r WHERE id = $id;");
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$u", uploaded);
        command.Parameters.AddWithValue("$f", failed);
        command.Parameters.AddWithValue("$r", result);
        command.Parameters.AddWithValue("$id", runId);
        command.ExecuteNonQuery();
    }

    // ---- settings ---------------------------------------------------------

    public string? GetSetting(string key)
    {
        using var command = Command("SELECT value FROM settings WHERE key = $k;");
        command.Parameters.AddWithValue("$k", key);
        return command.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var command = Command(
            "INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;");
        command.Parameters.AddWithValue("$k", key);
        command.Parameters.AddWithValue("$v", value);
        command.ExecuteNonQuery();
    }

    // ---- display for the UI ------------------------------------------------

    public IReadOnlyList<RunRecord> GetRecentRuns(int limit = 20)
    {
        using var command = Command(
            "SELECT id, started_utc, finished_utc, uploaded, failed, result FROM runs ORDER BY id DESC LIMIT $n;");
        command.Parameters.AddWithValue("$n", limit);
        using var reader = command.ExecuteReader();
        var result = new List<RunRecord>();
        while (reader.Read())
            result.Add(new RunRecord(
                reader.GetInt64(0),
                DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetInt32(3), reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return result;
    }

    public Dictionary<string, int> GetStatusCounts(long? sourceId = null)
    {
        using var command = Command(
            "SELECT status, COUNT(*) FROM files" + (sourceId is null ? "" : " WHERE source_id = $s") + " GROUP BY status;");
        if (sourceId is not null) command.Parameters.AddWithValue("$s", sourceId);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        while (reader.Read()) result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }

    /// Folders are only loaded when you expand them, with a summarised status per folder.
    public IReadOnlyList<FolderEntry> GetChildFolders(long sourceId, string relativeDirectory)
    {
        var prefix = relativeDirectory.Length == 0 ? "" : relativeDirectory.TrimEnd('/') + "/";
        using var command = Command("""
            SELECT substr(rest, 1, instr(rest, '/') - 1) AS folder,
                   COUNT(*),
                   SUM(CASE WHEN status = 'error' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END)
            FROM (SELECT substr(rel_path, length($prefix) + 1) AS rest, status
                  FROM files WHERE source_id = $s AND rel_path LIKE $like ESCAPE '\')
            WHERE instr(rest, '/') > 0
            GROUP BY folder ORDER BY folder;
            """);
        command.Parameters.AddWithValue("$s", sourceId);
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$like", EscapeLike(prefix) + "%");
        using var reader = command.ExecuteReader();
        var result = new List<FolderEntry>();
        while (reader.Read())
            result.Add(new FolderEntry(reader.GetString(0), prefix + reader.GetString(0),
                reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        return result;
    }

    public IReadOnlyList<FileEntry> GetFilesIn(long sourceId, string relativeDirectory)
    {
        var prefix = relativeDirectory.Length == 0 ? "" : relativeDirectory.TrimEnd('/') + "/";
        using var command = Command("""
            SELECT rel_path, size, status, last_sync_utc, last_error FROM files
            WHERE source_id = $s AND rel_path LIKE $like ESCAPE '\'
              AND instr(substr(rel_path, length($prefix) + 1), '/') = 0
            ORDER BY rel_path;
            """);
        command.Parameters.AddWithValue("$s", sourceId);
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$like", EscapeLike(prefix) + "%");
        using var reader = command.ExecuteReader();
        var result = new List<FileEntry>();
        while (reader.Read())
        {
            var relative = reader.GetString(0);
            result.Add(new FileEntry(relative, relative[(relative.LastIndexOf('/') + 1)..],
                reader.GetInt64(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return result;
    }

    public IReadOnlyList<FileEntry> GetFailedFiles(int limit = 200)
    {
        using var command = Command(
            "SELECT rel_path, size, status, last_sync_utc, last_error FROM files " +
            "WHERE status = 'error' ORDER BY rel_path LIMIT $n;");
        command.Parameters.AddWithValue("$n", limit);
        using var reader = command.ExecuteReader();
        var result = new List<FileEntry>();
        while (reader.Read())
        {
            var relative = reader.GetString(0);
            result.Add(new FileEntry(relative, relative[(relative.LastIndexOf('/') + 1)..],
                reader.GetInt64(1), reader.GetString(2), null,
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return result;
    }

    /// Closes the file for real: the connection pool would otherwise keep the handle (and a -wal file) alive.
    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
    }

    /// ".." resolved and no trailing slash, so the same folder is never stored twice.
    private static string NormalisePath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.Length > 1 ? full.TrimEnd('/') : full;
    }

    /// Escapes LIKE wildcards so a folder named a_b does not also match axb.
    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
