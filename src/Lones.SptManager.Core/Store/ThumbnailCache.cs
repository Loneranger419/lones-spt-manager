using System.Security.Cryptography;

namespace Lones.SptManager.Core.Store;

public static class ThumbnailCache
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "files.sp-mod.com",
        "sp-mod.com"
    };

    public static string Root(string managerData) => Path.Combine(managerData, "cache", "thumbnails");

    public static bool IsAllowedUrl(string? url)
        => TryParseAllowed(url, out _);

    public static string? TryLocalPath(string managerData, string? url)
    {
        if (!TryParseAllowed(url, out var uri))
        {
            return null;
        }

        var path = LocalPath(managerData, uri);
        return File.Exists(path) ? path : null;
    }

    public static string LocalPathFor(string managerData, string url)
    {
        if (!TryParseAllowed(url, out var uri))
        {
            throw new InvalidOperationException("Thumbnail URL is not an allowed Forge CDN host.");
        }

        return LocalPath(managerData, uri);
    }

    public static void WriteModJsonThumbnail(string managerData, ModDocument document, string thumbnailUrl)
    {
        var updated = new ModDocument
        {
            ManifestVersion = document.ManifestVersion,
            ModKey = document.ModKey,
            DisplayName = document.DisplayName,
            Version = document.Version,
            Kind = document.Kind,
            Deployable = document.Deployable,
            ArchiveHash = document.ArchiveHash,
            SourceArchive = document.SourceArchive,
            ForgeModId = document.ForgeModId,
            ForgeGuid = document.ForgeGuid,
            ThumbnailUrl = thumbnailUrl,
            WrapperFolder = document.WrapperFolder,
            Warnings = document.Warnings,
            Files = document.Files,
            ImportedAtUtc = document.ImportedAtUtc
        };
        WriteDocument(managerData, updated);
    }

    public static void WriteModJsonForgeInfo(string managerData, ModDocument document, string? displayName, string? thumbnailUrl)
    {
        var updated = new ModDocument
        {
            ManifestVersion = document.ManifestVersion,
            ModKey = document.ModKey,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? document.DisplayName : displayName.Trim(),
            Version = document.Version,
            Kind = document.Kind,
            Deployable = document.Deployable,
            ArchiveHash = document.ArchiveHash,
            SourceArchive = document.SourceArchive,
            ForgeModId = document.ForgeModId,
            ForgeGuid = document.ForgeGuid,
            ThumbnailUrl = string.IsNullOrWhiteSpace(thumbnailUrl) ? document.ThumbnailUrl : thumbnailUrl,
            WrapperFolder = document.WrapperFolder,
            Warnings = document.Warnings,
            Files = document.Files,
            ImportedAtUtc = document.ImportedAtUtc
        };
        WriteDocument(managerData, updated);
    }

    private static void WriteDocument(string managerData, ModDocument updated)
    {
        var dir = ModStore.PackageDirectory(managerData, updated.ModKey, updated.Version);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "mod.json"),
            System.Text.Json.JsonSerializer.Serialize(updated, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));
    }

    private static bool TryParseAllowed(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !AllowedHosts.Contains(parsed.Host))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static string LocalPath(string managerData, Uri uri)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..16];
        var ext = Path.GetExtension(uri.AbsolutePath);
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp"))
        {
            ext = ".img";
        }

        return Path.Combine(Root(managerData), hash + ext.ToLowerInvariant());
    }
}
