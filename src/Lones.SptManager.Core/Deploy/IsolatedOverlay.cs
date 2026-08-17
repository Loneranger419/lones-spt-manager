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

    public const string ConfigurationManagerCfg = "com.bepis.bepinex.configurationmanager.cfg";
    public const string BepInExCfg = "BepInEx.cfg";

    /// <summary>
    /// SPT needs F12 and a hidden BepInEx Manager object. An empty profile config gets
    /// BepInEx defaults (F1, HideManagerGameObject = false) and the F-key menu never shows.
    /// </summary>
    public static void SeedSptClientDefaults(string profileConfigDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileConfigDir);
        Directory.CreateDirectory(profileConfigDir);
        SeedConfigurationManagerHotkey(profileConfigDir);
        SeedHideManagerGameObject(profileConfigDir);
    }

    private static void SeedConfigurationManagerHotkey(string profileConfigDir)
    {
        var path = Path.Combine(profileConfigDir, ConfigurationManagerCfg);
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(path, """
            ## Seeded by Lone's SPT Manager so F12 opens Configuration Manager.
            ## Plugin GUID: com.bepis.bepinex.configurationmanager

            [General]

            ## The shortcut used to toggle the config manager window on and off.
            # Setting type: KeyboardShortcut
            # Default value: F12
            Show config manager = F12

            """);
    }

    private static void SeedHideManagerGameObject(string profileConfigDir)
    {
        var path = Path.Combine(profileConfigDir, BepInExCfg);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, """
                [Chainloader]

                ## SPT / EFT destroy a visible BepInEx Manager object; keep it hidden so F12 works.
                # Setting type: Boolean
                # Default value: true
                HideManagerGameObject = true

                """);
            return;
        }

        var text = File.ReadAllText(path);
        if (text.Contains("HideManagerGameObject", StringComparison.OrdinalIgnoreCase))
        {
            var updated = text.Replace("HideManagerGameObject = false", "HideManagerGameObject = true", StringComparison.OrdinalIgnoreCase);
            if (!text.Equals(updated, StringComparison.Ordinal))
            {
                File.WriteAllText(path, updated);
            }

            return;
        }

        File.AppendAllText(path, """

            [Chainloader]
            HideManagerGameObject = true

            """);
    }

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
