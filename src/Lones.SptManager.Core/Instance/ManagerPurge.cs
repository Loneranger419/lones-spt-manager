using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Instance;

public sealed class ManagerPurgeResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
}

public static class ManagerPurge
{
    public static ManagerPurgeResult Run(string managerData, string? gameRoot, IProcessLock? processLock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        var lockHandle = processLock ?? new SptProcessLock();
        var running = lockHandle.RunningSptProcesses();
        if (running.Count > 0)
        {
            return Fail("Can't purge while SPT is running: " + string.Join(", ", running));
        }

        managerData = Path.GetFullPath(managerData);
        gameRoot = string.IsNullOrWhiteSpace(gameRoot) ? null : Path.GetFullPath(gameRoot);
        var safety = Validate(managerData, gameRoot);
        if (safety is not null)
        {
            return Fail(safety);
        }

        if (gameRoot is not null && Directory.Exists(gameRoot))
        {
            try
            {
                var detached = new DeployEngine(lockHandle).DetachAll(gameRoot, managerData);
                if (detached.Status is DeployStatus.Failed or DeployStatus.BlockedProcesses)
                {
                    return Fail(detached.Message ?? "Could not detach manager junctions from the game folder.");
                }
            }
            catch (Exception ex)
            {
                return Fail("Could not detach manager junctions: " + ex.Message);
            }
        }

        try
        {
            WipeManagerData(managerData);
        }
        catch (Exception ex)
        {
            return Fail("Could not delete manager data: " + ex.Message);
        }

        Directory.CreateDirectory(managerData);
        return new ManagerPurgeResult
        {
            Success = true,
            Message = "Purged all manager data. The SPT install was left in place. Bind the game root to start fresh."
        };
    }

    private static readonly string[] ManagerChildren = ["store", "profiles", "instances", "cache"];

    private static void WipeManagerData(string managerData)
    {
        if (!Directory.Exists(managerData))
        {
            return;
        }

        foreach (var name in ManagerChildren)
        {
            var child = Path.Combine(managerData, name);
            if (Directory.Exists(child) || NtfsLinks.IsJunction(child))
            {
                SafeFileSystem.DeleteDirectoryNoFollow(child);
            }
        }

        var keep = KeepNames(managerData);
        foreach (var entry in Directory.EnumerateFileSystemEntries(managerData).ToArray())
        {
            if (keep.Contains(Path.GetFileName(entry)))
            {
                continue;
            }

            if (Directory.Exists(entry) || NtfsLinks.IsJunction(entry))
            {
                SafeFileSystem.DeleteDirectoryNoFollow(entry);
            }
            else
            {
                File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }

        if (keep.Count == 0)
        {
            SafeFileSystem.DeleteDirectoryNoFollow(managerData);
        }
    }

    /// <summary>
    /// The release zip (and a self-update) can live in the same folder as manager data.
    /// Never delete those files; deleting the running exe also fails the rest of the wipe.
    /// </summary>
    internal static bool IsInstallFile(string fileName)
        => fileName.Equals(ProductInfo.ExeFileName, StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("mods.json.example", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> KeepNames(string managerData)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Directory.EnumerateFiles(managerData))
        {
            var name = Path.GetFileName(entry);
            if (IsInstallFile(name))
            {
                keep.Add(name);
            }
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && File.Exists(processPath)
            && SafeFileSystem.SamePath(Path.GetDirectoryName(processPath)!, managerData))
        {
            keep.Add(Path.GetFileName(processPath));
        }

        return keep;
    }

    internal static string? Validate(string managerData, string? gameRoot)
    {
        var root = Path.GetPathRoot(managerData);
        if (root is not null
            && string.Equals(
                managerData.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return "Refusing to purge a drive root.";
        }

        if (gameRoot is not null && Directory.Exists(gameRoot))
        {
            if (SafeFileSystem.SamePath(gameRoot, managerData)
                || SafeFileSystem.IsUnderDirectory(gameRoot, managerData))
            {
                return "Refusing to purge: the SPT game folder is inside manager data.";
            }

            if (SafeFileSystem.SamePath(managerData, gameRoot)
                || SafeFileSystem.IsUnderDirectory(managerData, gameRoot))
            {
                return "Refusing to purge: manager data is inside the SPT game folder.";
            }
        }

        if (!Directory.Exists(managerData))
        {
            return null;
        }

        if (SafeFileSystem.SamePath(managerData, InstanceStore.DefaultManagerDataPath))
        {
            return null;
        }

        if (LooksLikeManagerData(managerData) || IsEmpty(managerData))
        {
            return null;
        }

        return "Refusing to purge: that folder does not look like Lone's SPT Manager data.";
    }

    private static bool LooksLikeManagerData(string managerData)
        => Directory.Exists(Path.Combine(managerData, "store"))
           || Directory.Exists(Path.Combine(managerData, "profiles"))
           || Directory.Exists(Path.Combine(managerData, "instances"))
           || Directory.Exists(Path.Combine(managerData, "cache"));

    private static bool IsEmpty(string managerData)
        => !Directory.EnumerateFileSystemEntries(managerData).Any();

    private static ManagerPurgeResult Fail(string message)
        => new() { Success = false, Message = message };
}
