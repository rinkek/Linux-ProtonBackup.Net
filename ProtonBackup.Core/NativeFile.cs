using System.Runtime.InteropServices;

namespace ProtonBackup.Core;

/// .NET has no inode number or file type. On Linux, st_ino in struct stat is always at offset 8
/// on both x86_64 and arm64; st_mode is at 24 on x86_64 and at 16 on arm64. The inode is stored
/// so that renames can be recognised later without hashing the contents.
internal static class NativeFile
{
    private const uint TypeMask = 0xF000;
    private const uint RegularFile = 0x8000;

    [DllImport("libc", EntryPoint = "stat", CharSet = CharSet.Ansi)]
    private static extern int Stat(string path, byte[] buffer);

    public static long? GetInode(string path)
    {
        var buffer = new byte[144];
        return Stat(path, buffer) == 0 ? BitConverter.ToInt64(buffer, 8) : null;
    }

    /// True for an ordinary file. Named pipes and sockets are not: the CLI would block reading a pipe.
    /// When the type cannot be determined the file is given the benefit of the doubt.
    public static bool IsRegularFile(string path)
    {
        var buffer = new byte[144];
        if (Stat(path, buffer) != 0) return true;
        var offset = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16 : 24;
        return (BitConverter.ToUInt32(buffer, offset) & TypeMask) == RegularFile;
    }
}
