using System.Text.Json;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Profiles;

public sealed class ProfileRuntimeDocument
{
    public required string ModKey { get; init; }
    public IReadOnlyList<ModFileRecord> Files { get; init; } = [];
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public static class ProfileRuntimeStore
{
    public static string Root(string managerData, string profileId)
        => Path.Combine(ProfilePaths.ProfileRoot(managerData, profileId), "runtime");

    public static string ModDirectory(string managerData, string profileId, string modKey)
        => Path.Combine(Root(managerData, profileId), Sanitize(modKey));

    public static string FilesDirectory(string managerData, string profileId, string modKey)
        => Path.Combine(ModDirectory(managerData, profileId, modKey), "files");

    public static void ImportLegacyStoreRuntime(string managerData, string profileId)
    {
        profileId = ProfilePaths.Sanitize(profileId);
        foreach (var document in ModStore.List(managerData)
                     .Where(item => HarvestRules.IsRuntimeVersion(item.Version)))
        {
            if (TryRead(managerData, profileId, document.ModKey) is { Files.Count: > 0 })
            {
                continue;
            }

            var source = ModStore.FilesDirectory(managerData, document.ModKey, document.Version);
            if (!Directory.Exists(source))
            {
                continue;
            }

            WriteFromTree(managerData, profileId, document.ModKey, source, document.Files);
        }
    }

    public static ProfileRuntimeDocument? TryRead(string managerData, string profileId, string modKey)
    {
        var path = ManifestPath(managerData, profileId, modKey);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ProfileRuntimeDocument>(File.ReadAllText(path), ProfileStore.JsonOptions);
    }

    public static IReadOnlyList<ProfileRuntimeDocument> List(string managerData, string profileId)
    {
        var root = Root(managerData, ProfilePaths.Sanitize(profileId));
        if (!Directory.Exists(root))
        {
            return [];
        }

        var list = new List<ProfileRuntimeDocument>();
        foreach (var manifest in Directory.EnumerateFiles(root, "runtime.json", SearchOption.AllDirectories))
        {
            var document = JsonSerializer.Deserialize<ProfileRuntimeDocument>(
                File.ReadAllText(manifest),
                ProfileStore.JsonOptions);
            if (document is not null)
            {
                list.Add(document);
            }
        }

        return list;
    }

    public static int FileCount(string managerData, string profileId, string modKey)
        => TryRead(managerData, profileId, modKey)?.Files.Count ?? 0;

    public static ProfileRuntimeDocument UpsertFile(
        string managerData,
        string profileId,
        string modKey,
        string canonical,
        string sourcePath,
        string sha256)
    {
        profileId = ProfilePaths.Sanitize(profileId);
        modKey = Sanitize(modKey);
        canonical = GamePath.Normalize(canonical);
        var dest = GamePath.Combine(FilesDirectory(managerData, profileId, modKey), canonical);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(sourcePath, dest, overwrite: true);

        var records = (TryRead(managerData, profileId, modKey)?.Files ?? [])
            .ToDictionary(file => GamePath.Normalize(file.CanonicalPath), StringComparer.OrdinalIgnoreCase);
        records[canonical] = new ModFileRecord { CanonicalPath = canonical, Sha256 = sha256 };
        return WriteDocument(managerData, profileId, modKey, records.Values);
    }

    public static void CopyMod(string managerData, string sourceId, string destinationId, string modKey)
    {
        sourceId = ProfilePaths.Sanitize(sourceId);
        destinationId = ProfilePaths.Sanitize(destinationId);
        modKey = Sanitize(modKey);
        var source = TryRead(managerData, sourceId, modKey)
                     ?? throw new InvalidOperationException($"No generated files for {modKey} on profile {sourceId}.");
        var sourceFiles = FilesDirectory(managerData, sourceId, modKey);
        if (!Directory.Exists(sourceFiles))
        {
            throw new InvalidOperationException($"Generated files for {modKey} on profile {sourceId} are missing.");
        }

        WriteFromTree(managerData, destinationId, modKey, sourceFiles, source.Files);
    }

    public static void CopyAll(string managerData, string sourceId, string destinationId)
    {
        foreach (var document in List(managerData, sourceId))
        {
            CopyMod(managerData, sourceId, destinationId, document.ModKey);
        }
    }

    private static void WriteFromTree(
        string managerData,
        string profileId,
        string modKey,
        string sourceFiles,
        IReadOnlyList<ModFileRecord> records)
    {
        var destFiles = FilesDirectory(managerData, profileId, modKey);
        if (Directory.Exists(destFiles) || NtfsLinks.IsJunction(destFiles))
        {
            SafeFileSystem.DeleteDirectoryNoFollow(destFiles);
        }

        IsolatedOverlay.CopyDirectoryNoFollow(sourceFiles, destFiles, skipExisting: false);
        WriteDocument(managerData, profileId, modKey, records);
    }

    private static ProfileRuntimeDocument WriteDocument(
        string managerData,
        string profileId,
        string modKey,
        IEnumerable<ModFileRecord> records)
    {
        var document = new ProfileRuntimeDocument
        {
            ModKey = Sanitize(modKey),
            Files = records
                .OrderBy(file => file.CanonicalPath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        Directory.CreateDirectory(ModDirectory(managerData, profileId, document.ModKey));
        File.WriteAllText(
            ManifestPath(managerData, profileId, document.ModKey),
            JsonSerializer.Serialize(document, ProfileStore.JsonOptions));
        return document;
    }

    private static string ManifestPath(string managerData, string profileId, string modKey)
        => Path.Combine(ModDirectory(managerData, profileId, modKey), "runtime.json");

    private static string Sanitize(string value)
    {
        var cleaned = string.Concat(value.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))).Trim();
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }
}
