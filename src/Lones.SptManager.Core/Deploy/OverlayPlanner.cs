using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Deploy;

public sealed class OverlayPlan
{
    public required IReadOnlyList<JunctionRecord> Junctions { get; init; }
    public required IReadOnlyList<CopiedFileRecord> CopiedFiles { get; init; }
}

public static class OverlayPlanner
{
    public const string BepInExPlugins = "BepInEx/plugins";

    public static OverlayPlan FromStaging(string stagingRoot)
    {
        var junctions = new List<JunctionRecord>();
        var copies = new List<CopiedFileRecord>();

        TryDirectoryJunction(junctions, stagingRoot, SptLayout.UserMods);
        TryDirectoryJunction(junctions, stagingRoot, SptLayout.UserPatchers);

        var stagingPlugins = GamePath.Combine(stagingRoot, BepInExPlugins);
        if (Directory.Exists(stagingPlugins))
        {
            foreach (var dir in Directory.EnumerateDirectories(stagingPlugins))
            {
                var name = Path.GetFileName(dir);
                if (name.Equals("spt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = BepInExPlugins + "/" + name;
                junctions.Add(new JunctionRecord
                {
                    InstallRelative = relative,
                    StagingRelative = relative,
                    TargetFull = Path.GetFullPath(dir)
                });
            }

            // Fallback only: files we refused to wrap (name would collide with SPT-owned spt\).
            foreach (var file in Directory.EnumerateFiles(stagingPlugins))
            {
                var name = Path.GetFileName(file);
                copies.Add(new CopiedFileRecord
                {
                    InstallRelative = BepInExPlugins + "/" + name,
                    Sha256 = HashFile(file)
                });
            }
        }

        if (Directory.Exists(stagingRoot))
        {
            foreach (var file in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
            {
                var relative = GamePath.Normalize(Path.GetRelativePath(stagingRoot, file));
                if (IsCoveredByPlannedOverlay(relative, junctions)
                    || copies.Any(copy => copy.InstallRelative.Equals(relative, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                copies.Add(new CopiedFileRecord
                {
                    InstallRelative = relative,
                    Sha256 = HashFile(file)
                });
            }
        }

        return new OverlayPlan { Junctions = junctions, CopiedFiles = copies };
    }

    /// <summary>
    /// Move a loose <c>BepInEx/plugins/Mod.dll</c> into <c>BepInEx/plugins/Mod/Mod.dll</c>
    /// so deploy can junction the folder. Never wrap into SPT-owned <c>spt\</c>.
    /// </summary>
    public static string WrapPluginCanonical(string canonical)
    {
        var path = GamePath.Normalize(canonical);
        if (!GamePath.IsUnderOrEqual(path, BepInExPlugins) || GamePath.EqualsNormalized(path, BepInExPlugins))
        {
            return path;
        }

        var rest = path[BepInExPlugins.Length..].Trim('/');
        if (rest.Length == 0 || rest.Contains('/'))
        {
            return path;
        }

        var folder = Path.GetFileNameWithoutExtension(rest);
        if (string.IsNullOrWhiteSpace(folder) || folder.Equals("spt", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return BepInExPlugins + "/" + folder + "/" + rest;
    }

    /// <summary>
    /// First-level plugin or <c>user/mods</c> folder a packaged file lives under, or null.
    /// </summary>
    public static string? TryOverlayFolder(string canonical)
    {
        var path = WrapPluginCanonical(GamePath.Normalize(canonical));
        foreach (var root in new[] { SptLayout.UserMods, BepInExPlugins })
        {
            if (!GamePath.IsUnderOrEqual(path, root) || GamePath.EqualsNormalized(path, root))
            {
                continue;
            }

            var rest = path[root.Length..].Trim('/');
            var slash = rest.IndexOf('/');
            var folder = slash < 0 ? rest : rest[..slash];
            if (folder.Length == 0 || folder.Equals("spt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return root + "/" + folder;
        }

        return null;
    }

    public static HashSet<string> OverlayFolders(IEnumerable<string> canonicalPaths)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in canonicalPaths)
        {
            var folder = TryOverlayFolder(path);
            if (folder is not null)
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    /// <summary>
    /// Delete empty real leftover folders under <c>user/mods</c> and <c>BepInEx/plugins</c>.
    /// Does not follow junctions or remove <c>spt\</c>.
    /// </summary>
    public static void PruneEmptyOverlayChildren(string treeRoot)
    {
        PruneEmptyFirstLevel(GamePath.Combine(treeRoot, SptLayout.UserMods), keepName: null);
        PruneEmptyFirstLevel(GamePath.Combine(treeRoot, BepInExPlugins), keepName: "spt");
    }

    public static bool IsPluginOverlayDirectory(string relative)
    {
        var path = GamePath.Normalize(relative);
        if (!GamePath.IsUnderOrEqual(path, BepInExPlugins) || GamePath.EqualsNormalized(path, BepInExPlugins))
        {
            return false;
        }

        var rest = path[BepInExPlugins.Length..].Trim('/');
        return rest.Length > 0
               && !rest.Contains('/')
               && !rest.Equals("spt", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCoveredByPlannedOverlay(string relative, IEnumerable<JunctionRecord> junctions)
    {
        var path = GamePath.Normalize(relative);
        if (path.Equals(ProfilePaths.StagingMarkerName, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/" + ProfilePaths.StagingMarkerName, StringComparison.OrdinalIgnoreCase)
            || GamePath.IsUnderOrEqual(path, SptLayout.BepInExConfig)
            || GamePath.IsUnderOrEqual(path, SptLayout.UserProfiles))
        {
            return true;
        }

        return junctions.Any(junction =>
            GamePath.IsUnderOrEqual(path, junction.StagingRelative)
            || GamePath.IsUnderOrEqual(path, junction.InstallRelative));
    }

    private static void TryDirectoryJunction(
        List<JunctionRecord> junctions,
        string stagingRoot,
        string relative)
    {
        var stagingDir = GamePath.Combine(stagingRoot, relative);
        if (!Directory.Exists(stagingDir) || !HasAnyFiles(stagingDir))
        {
            return;
        }

        junctions.Add(new JunctionRecord
        {
            InstallRelative = relative,
            StagingRelative = relative,
            TargetFull = Path.GetFullPath(stagingDir)
        });
    }

    private static void PruneEmptyFirstLevel(string root, string? keepName)
    {
        if (!Directory.Exists(root) || NtfsLinks.IsJunction(root))
        {
            return;
        }

        foreach (var child in Directory.EnumerateDirectories(root).ToArray())
        {
            if (NtfsLinks.IsJunction(child)
                || (keepName is not null
                    && Path.GetFileName(child).Equals(keepName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            PruneEmptyTree(child);
        }
    }

    private static void PruneEmptyTree(string directory)
    {
        if (!Directory.Exists(directory) || NtfsLinks.IsJunction(directory))
        {
            return;
        }

        foreach (var child in Directory.EnumerateDirectories(directory).ToArray())
        {
            if (!NtfsLinks.IsJunction(child))
            {
                PruneEmptyTree(child);
            }
        }

        if (!NtfsLinks.IsJunction(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static bool HasAnyFiles(string directory)
        => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
}
