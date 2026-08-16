using System.Security.Cryptography;
using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Deploy;

public sealed class StagingMergeResult
{
    public required IReadOnlyList<StagedFileRecord> Files { get; init; }
    public required IReadOnlyList<OverlayConflict> Conflicts { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public static class StagingMerger
{
    public static StagingMergeResult Rebuild(string managerData, string stagingRoot, IReadOnlyList<EnabledMod> enabled)
    {
        if (Directory.Exists(stagingRoot) || NtfsLinks.IsJunction(stagingRoot))
        {
            if (NtfsLinks.IsJunction(stagingRoot))
            {
                throw new InvalidOperationException("Staging root must be a real directory, not a junction.");
            }

            SafeFileSystem.DeleteDirectoryNoFollow(stagingRoot);
        }

        Directory.CreateDirectory(stagingRoot);
        File.WriteAllText(Path.Combine(stagingRoot, ProfilePaths.StagingMarkerName), "lones-spt-manager");

        var winners = new Dictionary<string, StagedFileRecord>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<OverlayConflict>();
        var warnings = new List<string>();

        foreach (var mod in enabled.OrderBy(item => item.Priority).ThenBy(item => item.ModKey, StringComparer.OrdinalIgnoreCase))
        {
            var document = ModStore.TryRead(managerData, mod.ModKey, mod.Version);
            if (document is null)
            {
                throw new InvalidOperationException($"Store package missing: {mod.ModKey} {mod.Version}");
            }

            if (!document.Deployable)
            {
                warnings.Add($"Skipped non-deployable package {mod.ModKey} {mod.Version}.");
                continue;
            }

            var filesDir = ModStore.FilesDirectory(managerData, mod.ModKey, mod.Version);
            foreach (var record in document.Files)
            {
                var canonical = GamePath.Normalize(record.CanonicalPath);
                if (SptDenylist.IsForbidden(canonical))
                {
                    warnings.Add($"Denylist skipped {canonical} from {mod.ModKey}.");
                    continue;
                }

                var source = GamePath.Combine(filesDir, canonical);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException($"Store file missing: {canonical}", source);
                }

                var destCanonical = OverlayPlanner.WrapPluginCanonical(canonical);
                var dest = GamePath.Combine(stagingRoot, destCanonical);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest, overwrite: true);

                if (winners.TryGetValue(destCanonical, out var previous))
                {
                    conflicts.Add(new OverlayConflict
                    {
                        CanonicalPath = destCanonical,
                        WinnerModKey = mod.ModKey,
                        LoserModKey = previous.WinnerModKey
                    });
                }

                winners[destCanonical] = new StagedFileRecord
                {
                    CanonicalPath = destCanonical,
                    Sha256 = record.Sha256,
                    WinnerModKey = mod.ModKey
                };
            }
        }

        return Finish(winners, conflicts, warnings);
    }

    public static StagingMergeResult ApplyProfileRuntime(
        string managerData,
        string profileId,
        string stagingRoot,
        IReadOnlyList<EnabledMod> enabled,
        StagingMergeResult current)
    {
        var keys = enabled
            .Where(item => !HarvestRules.IsRuntimeVersion(item.Version))
            .Select(item => item.ModKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keys.Length == 0)
        {
            return current;
        }

        var winners = current.Files.ToDictionary(file => file.CanonicalPath, StringComparer.OrdinalIgnoreCase);
        var conflicts = current.Conflicts.ToList();
        var warnings = current.Warnings.ToList();

        foreach (var modKey in keys)
        {
            var document = ProfileRuntimeStore.TryRead(managerData, profileId, modKey);
            if (document is null || document.Files.Count == 0)
            {
                continue;
            }

            var filesDir = ProfileRuntimeStore.FilesDirectory(managerData, profileId, modKey);
            foreach (var record in document.Files)
            {
                var canonical = GamePath.Normalize(record.CanonicalPath);
                if (SptDenylist.IsForbidden(canonical))
                {
                    warnings.Add($"Denylist skipped {canonical} from {modKey} runtime.");
                    continue;
                }

                var source = GamePath.Combine(filesDir, canonical);
                if (!File.Exists(source))
                {
                    warnings.Add($"Profile runtime file missing: {canonical} ({modKey}).");
                    continue;
                }

                var destCanonical = OverlayPlanner.WrapPluginCanonical(canonical);
                var dest = GamePath.Combine(stagingRoot, destCanonical);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest, overwrite: true);

                if (winners.TryGetValue(destCanonical, out var previous)
                    && !previous.WinnerModKey.Equals(modKey, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add(new OverlayConflict
                    {
                        CanonicalPath = destCanonical,
                        WinnerModKey = modKey,
                        LoserModKey = previous.WinnerModKey
                    });
                }

                winners[destCanonical] = new StagedFileRecord
                {
                    CanonicalPath = destCanonical,
                    Sha256 = record.Sha256,
                    WinnerModKey = modKey
                };
            }
        }

        return Finish(winners, conflicts, warnings);
    }

    public static StagingMergeResult ApplyOverwrite(string stagingRoot, string overwriteRoot, StagingMergeResult current)
    {
        if (!Directory.Exists(overwriteRoot))
        {
            return current;
        }

        var winners = current.Files.ToDictionary(file => file.CanonicalPath, StringComparer.OrdinalIgnoreCase);
        var conflicts = current.Conflicts.ToList();
        var warnings = current.Warnings.ToList();

        foreach (var file in Directory.EnumerateFiles(overwriteRoot, "*", SearchOption.AllDirectories))
        {
            var canonical = GamePath.Normalize(Path.GetRelativePath(overwriteRoot, file));
            if (canonical.Equals(ProfilePaths.StagingMarkerName, StringComparison.OrdinalIgnoreCase)
                || SptDenylist.IsForbidden(canonical)
                || HarvestRules.IsSecret(canonical))
            {
                warnings.Add("Overwrite skipped " + canonical);
                continue;
            }

            var destCanonical = OverlayPlanner.WrapPluginCanonical(canonical);
            var dest = GamePath.Combine(stagingRoot, destCanonical);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);

            if (winners.TryGetValue(destCanonical, out var previous))
            {
                conflicts.Add(new OverlayConflict
                {
                    CanonicalPath = destCanonical,
                    WinnerModKey = "overwrite",
                    LoserModKey = previous.WinnerModKey
                });
            }

            winners[destCanonical] = new StagedFileRecord
            {
                CanonicalPath = destCanonical,
                Sha256 = HashFile(file),
                WinnerModKey = "overwrite"
            };
        }

        return Finish(winners, conflicts, warnings);
    }

    private static StagingMergeResult Finish(
        Dictionary<string, StagedFileRecord> winners,
        List<OverlayConflict> conflicts,
        List<string> warnings)
        => new()
        {
            Files = winners.Values.OrderBy(file => file.CanonicalPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            Conflicts = conflicts,
            Warnings = warnings
        };

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
