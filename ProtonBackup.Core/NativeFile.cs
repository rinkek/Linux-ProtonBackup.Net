using System.Runtime.InteropServices;

namespace ProtonBackup.Core;

/// .NET has no inode number. On Linux, st_ino in struct stat is always at offset 8,
/// on both x86_64 and arm64. The number is stored so that renames can be recognised
/// later without hashing the contents.
internal static class NativeFile
{
    [DllImport("libc", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int Stat(string path, byte[] buffer);

    public static long? GetInode(string path)
    {
        var buffer = new byte[144];
        return Stat(path, buffer) == 0 ? BitConverter.ToInt64(buffer, 8) : null;
    }
}
