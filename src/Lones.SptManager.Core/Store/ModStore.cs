using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lones.SptManager.Core.Mapping;
using Lones.SptManager.Core.Paths;

namespace Lones.SptManager.Core.Store;

public sealed class ModStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string StoreRoot(string managerData) => Path.Combine(managerData, "store");

    public static string PackageDirectory(string managerData, string modKey, string version)
        => Path.Combine(StoreRoot(managerData), Sanitize(modKey), Sanitize(version));

    public ModDocument Write(
        string managerData,
        PackageMap map,
        IReadOnlyDictionary<string, string> extractedCanonicalFiles,
        MapperOptions options,
        string? archiveHash,
        string? sourceArchive)
    {
        var key = options.ModKey ?? InferKey(map, sourceArchive);
        var version = options.Version ?? (archiveHash is { Length: >= 12 } ? archiveHash[..12] : "unknown");
        var packageDir = PackageDirectory(managerData, key, version);
        var filesDir = Path.Combine(packageDir, "files");
        if (Directory.Exists(packageDir))
        {
            Directory.Delete(packageDir, recursive: true);
        }

        Directory.CreateDirectory(filesDir);

        var records = new List<ModFileRecord>();
        foreach (var entry in map.DeployFiles)
        {
            var canonical = entry.CanonicalPath!;
            if (!extractedCanonicalFiles.TryGetValue(GamePath.Normalize(canonical), out var source))
            {
                throw new InvalidOperationException($"Mapped file was not extracted: {canonical}");
            }

            var dest = Path.Combine(filesDir, canonical.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
            records.Add(new ModFileRecord
            {
                CanonicalPath = GamePath.Normalize(canonical),
                Sha256 = HashFile(dest)
            });
        }

        var document = new ModDocument
        {
            ModKey = Sanitize(key),
            Version = Sanitize(version),
            Kind = map.Kind.ToString(),
            Deployable = map.Deployable,
            ArchiveHash = archiveHash,
            SourceArchive = sourceArchive is null ? null : Path.GetFileName(sourceArchive),
            ForgeModId = options.ForgeModId,
            ForgeGuid = options.ForgeGuid,
            ThumbnailUrl = options.ThumbnailUrl,
            WrapperFolder = map.WrapperFolder,
            Warnings = map.Warnings,
            Files = records,
            ImportedAtUtc = DateTimeOffset.UtcNow
        };

        File.WriteAllText(Path.Combine(packageDir, "mod.json"), JsonSerializer.Serialize(document, JsonOptions));
        return document;
    }

    public ModDocument Commit(
        string managerData,
        PackageMap map,
        IReadOnlyList<ModFileRecord> files,
        MapperOptions options,
        string? archiveHash,
        string? sourceArchive)
    {
        var key = Sanitize(options.ModKey ?? InferKey(map, sourceArchive));
        var version = Sanitize(options.Version ?? (archiveHash is { Length: >= 12 } ? archiveHash[..12] : "unknown"));
        var packageDir = PackageDirectory(managerData, key, version);
        Directory.CreateDirectory(packageDir);
        var document = new ModDocument
        {
            ModKey = key,
            Version = version,
            Kind = map.Kind.ToString(),
            Deployable = map.Deployable,
            ArchiveHash = archiveHash,
            SourceArchive = sourceArchive is null ? null : Path.GetFileName(sourceArchive),
            ForgeModId = options.ForgeModId,
            ForgeGuid = options.ForgeGuid,
            ThumbnailUrl = options.ThumbnailUrl,
            WrapperFolder = map.WrapperFolder,
            Warnings = map.Warnings,
            Files = files,
            ImportedAtUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(Path.Combine(packageDir, "mod.json"), JsonSerializer.Serialize(document, JsonOptions));
        return document;
    }

    public static string ResolveKey(PackageMap map, MapperOptions options, string? sourceArchive)
        => Sanitize(options.ModKey ?? InferKey(map, sourceArchive));

    public static string ResolveVersion(MapperOptions options, string? archiveHash)
        => Sanitize(options.Version ?? (archiveHash is { Length: >= 12 } ? archiveHash[..12] : "unknown"));

    public static string FilesDirectory(string managerData, string modKey, string version)
        => Path.Combine(PackageDirectory(managerData, modKey, version), "files");

    public static ModDocument? TryRead(string managerData, string modKey, string version)
    {
        var path = Path.Combine(PackageDirectory(managerData, modKey, version), "mod.json");
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ModDocument>(File.ReadAllText(path), JsonOptions);
    }

    public static IReadOnlyList<ModDocument> List(string managerData)
    {
        var root = StoreRoot(managerData);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var list = new List<ModDocument>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "mod.json", SearchOption.AllDirectories).ToArray();
        }
        catch (Exception)
        {
            return list;
        }

        foreach (var modJson in files)
        {
            try
            {
                var document = JsonSerializer.Deserialize<ModDocument>(File.ReadAllText(modJson), JsonOptions);
                if (document is not null)
                {
                    list.Add(document);
                }
            }
            catch (Exception)
            {
                // One unreadable package must not empty the whole installed list.
            }
        }

        return list;
    }

    private static string InferKey(PackageMap map, string? sourceArchive)
    {
        foreach (var file in map.DeployFiles)
        {
            var path = file.CanonicalPath!;
            if (GamePath.IsUnderOrEqual(path, SptLayout.UserMods))
            {
                var rest = path[(SptLayout.UserMods.Length + 1)..];
                var folder = rest.Split('/')[0];
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    return folder;
                }
            }

            if (GamePath.IsUnderOrEqual(path, "BepInEx/plugins"))
            {
                var rest = path["BepInEx/plugins".Length..].Trim('/');
                var folder = rest.Split('/')[0];
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    return Path.GetFileNameWithoutExtension(folder);
                }
            }
        }

        if (sourceArchive is not null)
        {
            return Path.GetFileNameWithoutExtension(sourceArchive);
        }

        return "unknown-mod";
    }

    private static string Sanitize(string value)
    {
        var cleaned = string.Concat(value.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))).Trim();
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
