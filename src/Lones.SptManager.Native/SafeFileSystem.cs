namespace Lones.SptManager.Native;

/// <summary>
/// Directory deletes that never recurse through a junction into its target.
/// </summary>
public static class SafeFileSystem
{
    public static bool SamePath(string left, string right)
    {
        var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnderDirectory(string path, string parentDirectory)
    {
        var child = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
        var parent = Path.GetFullPath(parentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    public static void DeleteDirectoryNoFollow(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);

        if (NtfsLinks.IsJunction(full))
        {
            NtfsLinks.RemoveJunction(full);
            return;
        }

        if (!Directory.Exists(full))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(full))
        {
            DeleteFileRetry(file);
        }

        foreach (var child in Directory.EnumerateDirectories(full))
        {
            DeleteDirectoryNoFollow(child);
        }

        DeleteDirectoryRetry(full);
    }

    private static void DeleteFileRetry(string path)
    {
        File.SetAttributes(path, FileAttributes.Normal);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 8)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static void DeleteDirectoryRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path);
                }

                return;
            }
            catch (IOException) when (attempt < 8)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }
}
