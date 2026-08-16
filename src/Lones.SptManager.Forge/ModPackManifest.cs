using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lones.SptManager.Forge;

public sealed class ModPackManifest
{
    public string? Name { get; set; }
    public string? Title { get; set; }
    public string? ProfileName { get; set; }
    public List<ModPackEntry> Mods { get; set; } = [];

    public string? SuggestedProfileName
        => FirstNonEmpty(Name, Title, ProfileName);

    public IReadOnlyList<ModPackEntry> ListedMods()
    {
        var seen = new HashSet<int>();
        var list = new List<ModPackEntry>();
        foreach (var entry in Mods)
        {
            if (entry.Id <= 0 || !seen.Add(entry.Id))
            {
                continue;
            }

            list.Add(entry);
        }

        return list;
    }

    public static ModPackManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<ModPackManifest>(json, JsonOptions);
        if (manifest is null)
        {
            throw new InvalidOperationException("Pack JSON was empty.");
        }

        return manifest;
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed class ModPackEntry
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Side { get; set; }
    public string? InstalledVersion { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? SettingsNotes { get; set; }

    [JsonIgnore]
    public string? RequestedVersion
        => string.IsNullOrWhiteSpace(InstalledVersion) ? Version : InstalledVersion;

    [JsonIgnore]
    public string DisplayName => Name ?? Slug ?? ("mod " + Id);
}

public static class ModPackSource
{
    public const int MaxJsonBytes = 2 * 1024 * 1024;

    public static string Normalize(string source)
    {
        var trimmed = source.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Pack source is empty.");
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Pack URLs must be HTTPS.");
            }

            if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return uri.ToString();
            }

            if (uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                return LocalExisting(uri.LocalPath);
            }
        }

        if (File.Exists(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        if (LooksLikeWindowsPath(trimmed))
        {
            return LocalExisting(trimmed);
        }

        if (LooksLikeHostPath(trimmed)
            && Uri.TryCreate("https://" + trimmed.TrimStart('/'), UriKind.Absolute, out var https)
            && https.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && https.Host.Contains('.'))
        {
            return https.ToString();
        }

        throw new InvalidOperationException("Pack source is not an HTTPS URL or an existing JSON file.");
    }

    public static bool IsHttpsUrl(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri)
           && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string LocalExisting(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new InvalidOperationException("Pack file not found: " + full);
        }

        return full;
    }

    private static bool LooksLikeWindowsPath(string source)
        => source.Contains('\\')
           || (source.Length >= 2 && char.IsLetter(source[0]) && source[1] == ':');

    private static bool LooksLikeHostPath(string source)
    {
        if (source.Contains('\\') || source.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        var first = source.Split('/')[0];
        if (!first.Contains('.') || first.StartsWith('.'))
        {
            return false;
        }

        return source.Contains('/')
               || !(first.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    || first.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }
}
