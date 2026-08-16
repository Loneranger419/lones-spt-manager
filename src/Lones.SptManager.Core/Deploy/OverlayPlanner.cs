using Lones.SptManager.Core.Paths;

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

    private static bool HasAnyFiles(string directory)
        => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
}
