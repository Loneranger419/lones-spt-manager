using System.Security.Cryptography;
using System.Text.Json;
using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Deploy;

public sealed class HarvestBaselineFile
{
    public required string RelativePath { get; init; }
    public required string Sha256 { get; init; }
}

public sealed class HarvestBaseline
{
    public int ManifestVersion { get; init; } = ProductInfo.ManifestVersion;
    public required string ProfileId { get; init; }
    public DateTimeOffset WrittenAtUtc { get; init; }
    public IReadOnlyList<HarvestBaselineFile> Files { get; init; } = [];
}

public sealed class HarvestedFile
{
    public required string CanonicalPath { get; init; }
    public required string Sha256 { get; init; }
}

public sealed class HarvestResult
{
    public required DeployStatus Status { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<HarvestedFile> Files { get; init; } = [];
    public IReadOnlyList<HarvestedFile> AssignedToMods { get; init; } = [];
    public IReadOnlyList<string> RunningProcesses { get; init; } = [];
}

public sealed class AssignOverwriteResult
{
    public required string ModKey { get; init; }
    public required int AssignedCount { get; init; }
}

public sealed class HarvestEngine
{
    private readonly IProcessLock _processLock;

    public HarvestEngine()
        : this(new SptProcessLock())
    {
    }

    public HarvestEngine(IProcessLock processLock)
    {
        _processLock = processLock;
    }

    public HarvestResult Harvest(string gameRoot, string managerData, string profileId, SptOwnedBaseline? baseline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        profileId = ProfilePaths.Sanitize(profileId);
        managerData = Path.GetFullPath(managerData);
        gameRoot = Path.GetFullPath(gameRoot);

        var running = _processLock.RunningSptProcesses();
        if (running.Count > 0)
        {
            return new HarvestResult
            {
                Status = DeployStatus.BlockedProcesses,
                Message = "Harvest blocked while SPT processes are running: " + string.Join(", ", running),
                RunningProcesses = running
            };
        }

        var baselinePath = ProfilePaths.HarvestBaseline(managerData, profileId);
        if (!File.Exists(baselinePath))
        {
            return new HarvestResult
            {
                Status = DeployStatus.Failed,
                Message = "No harvest baseline. Deploy this profile first."
            };
        }

        var last = JsonSerializer.Deserialize<HarvestBaseline>(File.ReadAllText(baselinePath), ProfileStore.JsonOptions);
        if (last is null)
        {
            return new HarvestResult { Status = DeployStatus.Failed, Message = "Harvest baseline is unreadable." };
        }

        baseline ??= File.Exists(GamePath.Combine(gameRoot, SptLayout.EscapeFromTarkovExe))
            ? new SptOwnedBaselineBuilder().Build(gameRoot)
            : new SptOwnedBaseline([]);

        var known = last.Files.ToDictionary(file => GamePath.Normalize(file.RelativePath), StringComparer.OrdinalIgnoreCase);
        var overwriteRoot = ProfilePaths.Overwrite(managerData, profileId);
        var store = ModStore.List(managerData).ToArray();
        var harvested = new List<HarvestedFile>();
        var assigned = new List<HarvestedFile>();
        assigned.AddRange(PromoteOverwriteConfigs(managerData, profileId, store)
            .Select(path => new HarvestedFile { CanonicalPath = path, Sha256 = string.Empty }));

        foreach (var (canonical, fullPath) in EnumerateHarvestable(gameRoot, managerData, profileId))
        {
            if (HarvestRules.ShouldIgnore(canonical, baseline))
            {
                continue;
            }

            var hash = HashFile(fullPath);
            if (known.TryGetValue(canonical, out var recorded) && string.Equals(recorded.Sha256, hash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ownerKey = HarvestRules.TryOwnedModKey(canonical, store);
            if (ownerKey is not null && TryAssignOwnedFile(managerData, profileId, ownerKey, canonical, fullPath, hash))
            {
                assigned.Add(new HarvestedFile { CanonicalPath = canonical, Sha256 = hash });
                continue;
            }

            var dest = GamePath.Combine(overwriteRoot, canonical);
            if (File.Exists(dest) && string.Equals(HashFile(dest), hash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(fullPath, dest, overwrite: true);
            harvested.Add(new HarvestedFile { CanonicalPath = canonical, Sha256 = hash });
        }

        return new HarvestResult
        {
            Status = DeployStatus.Success,
            Message = SummarizeHarvest(harvested.Count, assigned.Count),
            Files = harvested,
            AssignedToMods = assigned
        };
    }

    public static void WriteBaseline(
        string managerData,
        string profileId,
        string? gameRoot = null,
        IReadOnlyList<CopiedFileRecord>? copiedFiles = null)
    {
        profileId = ProfilePaths.Sanitize(profileId);
        var files = EnumerateHarvestable(gameRoot, managerData, profileId, copiedFiles)
            .Select(item => new HarvestBaselineFile
            {
                RelativePath = item.Canonical,
                Sha256 = HashFile(item.FullPath)
            })
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var document = new HarvestBaseline
        {
            ProfileId = profileId,
            WrittenAtUtc = DateTimeOffset.UtcNow,
            Files = files
        };
        var path = ProfilePaths.HarvestBaseline(managerData, profileId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, ProfileStore.JsonOptions));
    }

    public static IReadOnlyList<string> PromoteOverwriteConfigs(string managerData, string profileId)
        => PromoteOverwriteConfigs(managerData, profileId, ModStore.List(managerData).ToArray());

    private static IReadOnlyList<string> PromoteOverwriteConfigs(
        string managerData,
        string profileId,
        IReadOnlyList<ModDocument> store)
    {
        var moved = new List<string>();
        foreach (var canonical in ListOverwrite(managerData, profileId))
        {
            var ownerKey = HarvestRules.TryOwnedModKey(canonical, store);
            if (ownerKey is null)
            {
                continue;
            }

            var source = GamePath.Combine(ProfilePaths.Overwrite(managerData, profileId), canonical);
            if (!File.Exists(source))
            {
                continue;
            }

            if (TryAssignOwnedFile(managerData, profileId, ownerKey, canonical, source, HashFile(source)))
            {
                moved.Add(canonical);
            }
        }

        return moved;
    }

    public static IReadOnlyList<string> ListOverwrite(string managerData, string profileId)
    {
        var root = ProfilePaths.Overwrite(managerData, profileId);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => GamePath.Normalize(Path.GetRelativePath(root, file)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void DiscardAll(string managerData, string profileId)
    {
        var root = ProfilePaths.Overwrite(managerData, profileId);
        if (Directory.Exists(root) || NtfsLinks.IsJunction(root))
        {
            SafeFileSystem.DeleteDirectoryNoFollow(root);
        }
    }

    public static void DiscardPaths(string managerData, string profileId, IEnumerable<string> canonicalPaths)
    {
        var root = ProfilePaths.Overwrite(managerData, profileId);
        foreach (var canonical in canonicalPaths)
        {
            var path = GamePath.Combine(root, GamePath.Normalize(canonical));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        PruneEmptyDirectories(root);
    }

    /// <summary>
    /// Attach selected Overwrite files to an existing store package as per-profile generated files.
    /// Never mutates the hashed original and does not create a new store version.
    /// </summary>
    public static AssignOverwriteResult AssignToMod(
        string managerData,
        string profileId,
        string modKey,
        string version,
        IReadOnlyList<string> overwriteCanonicals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        ArgumentException.ThrowIfNullOrWhiteSpace(modKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (overwriteCanonicals.Count == 0)
        {
            throw new InvalidOperationException("Select at least one Overwrite file to assign.");
        }

        _ = ModStore.TryRead(managerData, modKey, version)
            ?? throw new InvalidOperationException($"Store package missing: {modKey} {version}");
        if (!ModStore.List(managerData).Any(document =>
                document.Deployable
                && document.ModKey.Equals(modKey, StringComparison.OrdinalIgnoreCase)
                && !HarvestRules.IsRuntimeVersion(document.Version)))
        {
            throw new InvalidOperationException("Store package is not deployable: " + modKey);
        }

        var overwriteRoot = ProfilePaths.Overwrite(managerData, profileId);
        var assigned = 0;
        foreach (var raw in overwriteCanonicals)
        {
            var canonical = OverlayPlanner.WrapPluginCanonical(raw);
            var from = GamePath.Combine(overwriteRoot, GamePath.Normalize(raw));
            if (!File.Exists(from))
            {
                throw new FileNotFoundException("Overwrite file missing: " + raw, from);
            }

            if (!TryAssignOwnedFile(managerData, profileId, modKey, canonical, from, HashFile(from)))
            {
                throw new InvalidOperationException("Could not attach Overwrite file to " + modKey + ": " + raw);
            }

            if (File.Exists(from))
            {
                File.Delete(from);
            }

            assigned++;
        }

        PruneEmptyDirectories(overwriteRoot);
        return new AssignOverwriteResult { ModKey = modKey, AssignedCount = assigned };
    }

    private static string SummarizeHarvest(int overwriteCount, int assignedCount)
    {
        if (overwriteCount == 0 && assignedCount == 0)
        {
            return "Harvest found nothing new.";
        }

        var parts = new List<string>();
        if (overwriteCount > 0)
        {
            parts.Add($"{overwriteCount} file(s) into Overwrite");
        }

        if (assignedCount > 0)
        {
            parts.Add($"{assignedCount} file(s) onto their mod (runtime)");
        }

        return "Harvested " + string.Join(", ", parts) + ".";
    }

    private static bool TryAssignOwnedFile(
        string managerData,
        string profileId,
        string modKey,
        string canonical,
        string sourcePath,
        string sha256)
    {
        if (!ModStore.List(managerData).Any(document =>
                document.Deployable
                && document.ModKey.Equals(modKey, StringComparison.OrdinalIgnoreCase)
                && !HarvestRules.IsRuntimeVersion(document.Version)))
        {
            return false;
        }

        ProfileRuntimeStore.UpsertFile(managerData, profileId, modKey, canonical, sourcePath, sha256);

        var staleOverwrite = GamePath.Combine(ProfilePaths.Overwrite(managerData, profileId), canonical);
        if (File.Exists(staleOverwrite))
        {
            File.Delete(staleOverwrite);
        }

        return true;
    }

    private static void PruneEmptyDirectories(string root)
    {
        if (!Directory.Exists(root) || NtfsLinks.IsJunction(root))
        {
            return;
        }

        foreach (var child in Directory.EnumerateDirectories(root).ToArray())
        {
            PruneEmptyDirectories(child);
        }

        if (!Directory.EnumerateFileSystemEntries(root).Any())
        {
            Directory.Delete(root);
        }
    }

    private static IEnumerable<(string Canonical, string FullPath)> EnumerateHarvestable(
        string? gameRoot,
        string managerData,
        string profileId,
        IReadOnlyList<CopiedFileRecord>? copiedFiles = null)
    {
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (copiedFiles is null)
        {
            var manifestPath = ProfilePaths.Manifest(managerData, profileId);
            if (File.Exists(manifestPath))
            {
                try
                {
                    copiedFiles = JsonSerializer.Deserialize<DeployManifest>(
                        File.ReadAllText(manifestPath),
                        ProfileStore.JsonOptions)?.CopiedFiles;
                }
                catch (JsonException)
                {
                    copiedFiles = null;
                }
            }
        }

        if (copiedFiles is not null && !string.IsNullOrWhiteSpace(gameRoot))
        {
            foreach (var copy in copiedFiles)
            {
                var canonical = GamePath.Normalize(copy.InstallRelative);
                copied.Add(canonical);
                var install = GamePath.Combine(gameRoot, canonical);
                if (File.Exists(install))
                {
                    yield return (canonical, install);
                }
            }
        }

        foreach (var item in EnumerateTree(ProfilePaths.Staging(managerData, profileId), prefix: null))
        {
            if (copied.Contains(item.Canonical))
            {
                continue;
            }

            yield return item;
        }

        foreach (var item in EnumerateTree(ProfilePaths.BepInExConfig(managerData, profileId), SptLayout.BepInExConfig))
        {
            yield return item;
        }
    }

    private static IEnumerable<(string Canonical, string FullPath)> EnumerateTree(string root, string? prefix)
    {
        if (!Directory.Exists(root) || NtfsLinks.IsJunction(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = GamePath.Normalize(Path.GetRelativePath(root, file));
            if (relative.Equals(ProfilePaths.StagingMarkerName, StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith("/" + ProfilePaths.StagingMarkerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var canonical = prefix is null ? relative : prefix + "/" + relative;
            yield return (GamePath.Normalize(canonical), file);
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
