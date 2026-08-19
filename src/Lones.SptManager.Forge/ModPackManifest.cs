using System.Text.Json;
using System.Text.Json.Serialization;
using Lones.SptManager.Core;

namespace Lones.SptManager.Forge;

public sealed class ModPackManifest
{
    public string? Name { get; set; }
    public string? Title { get; set; }
    public string? ProfileName { get; set; }
    public List<ModPackEntry> Mods { get; set; } = [];
    public List<ModPackEntry> Addons { get; set; } = [];

    public string? SuggestedProfileName
        => FirstNonEmpty(Name, Title, ProfileName);

    public IReadOnlyList<ModPackEntry> ListedMods()
    {
        var seen = new HashSet<(bool Addon, int Id)>();
        var list = new List<ModPackEntry>();
        foreach (var entry in Mods)
        {
            TryAdd(list, seen, entry, forceAddon: false);
        }

        foreach (var entry in Addons)
        {
            TryAdd(list, seen, entry, forceAddon: true);
        }

        return list;
    }

    private static void TryAdd(
        List<ModPackEntry> list,
        HashSet<(bool Addon, int Id)> seen,
        ModPackEntry entry,
        bool forceAddon)
    {
        if (entry.Id <= 0)
        {
            return;
        }

        var addon = forceAddon || entry.IsAddon;
        if (!seen.Add((addon, entry.Id)))
        {
            return;
        }

        list.Add(forceAddon && !entry.IsAddon ? entry.AsAddon() : entry);
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
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Side { get; set; }
    public string? InstalledVersion { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? SettingsNotes { get; set; }

    [JsonIgnore]
    public bool IsAddon
        => string.Equals(Kind, "addon", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string? RequestedVersion
        => string.IsNullOrWhiteSpace(InstalledVersion) ? Version : InstalledVersion;

    [JsonIgnore]
    public string DisplayName => Name ?? Slug ?? ((IsAddon ? "addon " : "mod ") + Id);

    public ModPackEntry AsAddon()
        => new()
        {
            Id = Id,
            Kind = "addon",
            Name = Name,
            Slug = Slug,
            Side = Side,
            InstalledVersion = InstalledVersion,
            Version = Version,
            Description = Description,
            SettingsNotes = SettingsNotes
        };
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

    public static async Task<string> ReadJsonAsync(string source, HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        var resolved = Normalize(source);
        if (!IsHttpsUrl(resolved))
        {
            var info = new FileInfo(resolved);
            if (info.Length > MaxJsonBytes)
            {
                throw new InvalidOperationException("Pack JSON is larger than 2 MB.");
            }

            return await File.ReadAllTextAsync(resolved, cancellationToken).ConfigureAwait(false);
        }

        var owns = http is null;
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            if (owns)
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.UserAgent);
            }

            using var response = await http.GetAsync(resolved, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxJsonBytes)
            {
                throw new InvalidOperationException("Pack JSON is larger than 2 MB.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var limited = new MemoryStream();
            var buffer = new byte[8192];
            var total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > MaxJsonBytes)
                {
                    throw new InvalidOperationException("Pack JSON is larger than 2 MB.");
                }

                limited.Write(buffer, 0, read);
            }

            limited.Position = 0;
            using var reader = new StreamReader(limited);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (owns)
            {
                http.Dispose();
            }
        }
    }

    public static async Task<ModPackManifest> LoadAsync(string source, HttpClient? http = null, CancellationToken cancellationToken = default)
        => ModPackManifest.Parse(await ReadJsonAsync(source, http, cancellationToken).ConfigureAwait(false));

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
