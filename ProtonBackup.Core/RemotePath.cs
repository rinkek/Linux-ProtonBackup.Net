namespace ProtonBackup.Core;

/// Proton Drive always uses POSIX paths; a / in a name is escaped with a backslash.
public static class RemotePath
{
    public static string Join(string parent, string name) =>
        parent.TrimEnd('/') + "/" + Escape(name);

    public static string Combine(string parent, string relativeDirectory)
    {
        var result = parent.TrimEnd('/');
        foreach (var segment in relativeDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries))
            result = Join(result, segment);
        return result;
    }

    public static string Escape(string name) => name.Replace("/", "\\/");

    public static string ParentOf(string path)
    {
        var index = path.LastIndexOf('/');
        return index <= 0 ? "/" : path[..index];
    }

    public static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];
}
