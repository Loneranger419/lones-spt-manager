using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Deploy;

public static class IsolatedOverlay
{
    public static IReadOnlyList<JunctionRecord> Plan(string managerData, string profileId)
    {
        return
        [
            new JunctionRecord
            {
                InstallRelative = SptLayout.UserProfiles,
                StagingRelative = "saves",
                TargetFull = Path.GetFullPath(ProfilePaths.Saves(managerData, profileId))
            },
            new JunctionRecord
            {
                InstallRelative = SptLayout.BepInExConfig,
                StagingRelative = "bepinex-config",
                TargetFull = Path.GetFullPath(ProfilePaths.BepInExConfig(managerData, profileId))
            }
        ];
    }

    public static OverlayPlan Combine(OverlayPlan staging, IReadOnlyList<JunctionRecord> isolated)
        => new()
        {
            Junctions = staging.Junctions.Concat(isolated).ToArray(),
            CopiedFiles = staging.CopiedFiles
        };

    /// <summary>
    /// Seed a profile-owned tree from a real install directory, then leave the install path gone
    /// so <see cref="NtfsLinks.CreateJunction"/> can recreate it.
    /// </summary>
    public static void ClaimInstallDirectory(string installPath, string profileTarget, string profilesRoot)
    {
        Directory.CreateDirectory(profileTarget);

        if (NtfsLinks.IsJunction(installPath))
        {
            var target = NtfsLinks.TryGetJunctionTarget(installPath);
            if (target is null
                || (!SafeFileSystem.IsUnderDirectory(target, profilesRoot) && !SafeFileSystem.SamePath(target, profilesRoot)))
            {
                throw new IOException("Refusing to replace an unknown junction: " + installPath);
            }

            NtfsLinks.RemoveJunction(installPath);
            return;
        }

        if (File.Exists(installPath))
        {
            throw new IOException("Overlay path is a file, not a directory: " + installPath);
        }

        if (!Directory.Exists(installPath))
        {
            return;
        }

        CopyDirectoryNoFollow(installPath, profileTarget, skipExisting: true);
        SafeFileSystem.DeleteDirectoryNoFollow(installPath);
    }

    public static void CopyDirectoryNoFollow(string source, string dest, bool skipExisting)
    {
        if (NtfsLinks.IsJunction(source))
        {
            throw new IOException("Refusing to copy through a junction: " + source);
        }

        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.Equals(ProfilePaths.StagingMarkerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(dest, name);
            if (skipExisting && File.Exists(target))
            {
                continue;
            }

            File.Copy(file, target, overwrite: !skipExisting);
        }

        foreach (var child in Directory.EnumerateDirectories(source))
        {
            if (NtfsLinks.IsJunction(child))
            {
                continue;
            }

            CopyDirectoryNoFollow(child, Path.Combine(dest, Path.GetFileName(child)), skipExisting);
        }
    }
}
