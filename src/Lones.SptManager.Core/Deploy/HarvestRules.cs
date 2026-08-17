using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Core.Deploy;

public static class HarvestRules
{
    public const string RuntimeVersion = "runtime";

    public static bool IsRuntimeVersion(string? version)
        => !string.IsNullOrWhiteSpace(version)
           && version.Equals(RuntimeVersion, StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> ProfileScopedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "BepInEx/config/BepInEx.cfg",
        "BepInEx/config/com.bepis.bepinex.configurationmanager.cfg"
    };

    private static readonly HashSet<string> FikaServerOwnedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SPT_Runtime/user/mods/fika-server/assets/configs/fika.jsonc",
        "SPT_Runtime/user/mods/fika-server/assets/configsfika.jsonc",
        "SPT_Runtime/user/mods/fika-server/database/friendRequests.json",
        "SPT_Runtime/user/mods/fika-server/database/playerRelations.json"
    };

    private static readonly HashSet<string> PinFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "config.json",
        "config.jsonc",
        "config.cfg",
        "config.ini",
        "config.toml",
        "blacklists.json",
        "params.json",
        "loader.json",
        "userDefinedNames.json"
    };

    private static readonly HashSet<string> PinExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json",
        ".jsonc",
        ".cfg",
        ".ini",
        ".toml"
    };

    private static readonly string[] PackageRoots =
    [
        SptLayout.UserMods + "/",
        "SPT/user/mods/",
        "user/mods/",
        "BepInEx/plugins/"
    ];

    private static readonly HashSet<string> SharedLibraryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "0Harmony",
        "HarmonyLib",
        "Newtonsoft.Json",
        "NLog",
        "protobuf-net"
    };

    private static readonly HashSet<string> WeakLastSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "plugin",
        "plugins",
        "config",
        "settings",
        "client",
        "server",
        "patch",
        "shared",
        "common",
        "helper",
        "utils",
        "utility",
        "library",
        "core",
        "mod"
    };

    public static bool IsSecret(string relativePath)
    {
        var normalized = GamePath.Normalize(relativePath);
        if (GamePath.IsUnderOrEqual(normalized, SptLayout.UserCerts)
            || GamePath.IsUnderOrEqual(normalized, SptLayout.UserCredentials)
            || GamePath.IsUnderOrEqual(normalized, SptLayout.SptData))
        {
            return true;
        }

        var name = Path.GetFileName(normalized);
        return name.Equals("credentials.json", StringComparison.OrdinalIgnoreCase)
               || name.Equals("server.key", StringComparison.OrdinalIgnoreCase)
               || name.Equals("server.crt", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProfileScoped(string relativePath)
        => ProfileScopedFiles.Contains(GamePath.Normalize(relativePath));

    public static bool ShouldStayInOverwrite(string relativePath)
        => IsGeneratedOrState(relativePath);

    public static bool IsGeneratedOrState(string relativePath)
    {
        var normalized = GamePath.Normalize(relativePath);
        if (HasDirectoryContaining(normalized, "DO_NOT_TOUCH")
            || HasDirectoryContaining(normalized, "GeneratedVanillaMappings")
            || HasDirectoryNamed(normalized, "logs")
            || HasDirectoryNamed(normalized, "wwwroot")
            || HasDirectoryNamed(normalized, "database"))
        {
            return true;
        }

        var name = Path.GetFileName(normalized);
        return name.Equals("state.json", StringComparison.OrdinalIgnoreCase)
               || name.Equals("generated.json", StringComparison.OrdinalIgnoreCase)
               || name.Equals("allNames.json", StringComparison.OrdinalIgnoreCase)
               || name.Equals("traits.json", StringComparison.OrdinalIgnoreCase)
               || name.Contains("restock", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
               || name.Equals("RELEASE_NOTES.txt", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldPinToMod(string relativePath)
    {
        var normalized = GamePath.Normalize(relativePath);
        if (IsGeneratedOrState(normalized) || IsSecret(normalized) || IsProfileScoped(normalized))
        {
            return false;
        }

        var name = Path.GetFileName(normalized);
        if (PinFileNames.Contains(name))
        {
            return true;
        }

        if ((HasDirectoryNamed(normalized, "config") || HasDirectoryNamed(normalized, "configs"))
            && PinExtensions.Contains(Path.GetExtension(normalized)))
        {
            return true;
        }

        return GamePath.IsUnderOrEqual(normalized, SptLayout.BepInExConfig)
               && Path.GetExtension(normalized).Equals(".cfg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPinStyleFile(string normalized)
        => ShouldPinToMod(normalized)
           || PinFileNames.Contains(Path.GetFileName(normalized))
           || PinExtensions.Contains(Path.GetExtension(normalized));

    public static string? TryOwnedModKey(string relativePath)
        => TryOwnedModKey(relativePath, store: null);

    public static string? TryOwnedModKey(string relativePath, IReadOnlyList<ModDocument>? store)
    {
        var normalized = GamePath.Normalize(relativePath);
        if (IsSecret(normalized) || IsProfileScoped(normalized))
        {
            return null;
        }

        var fikaOwned = FikaServerOwnedFiles.Contains(normalized);
        if (!fikaOwned && IsGeneratedOrState(normalized))
        {
            return null;
        }

        if (store is { Count: > 0 })
        {
            var owner = TryResolveStoreOwner(normalized, store);
            if (owner is not null)
            {
                return owner;
            }
        }

        return fikaOwned ? "fika-server" : null;
    }

    public static string? TryResolveStoreOwner(string relativePath, IReadOnlyList<ModDocument> store)
    {
        var normalized = GamePath.Normalize(relativePath);
        var candidates = store
            .Where(document => document.Deployable && !IsRuntimeVersion(document.Version))
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var exactPath = UniqueModKey(candidates.Where(document =>
            document.Files.Any(file => GamePath.EqualsNormalized(file.CanonicalPath, normalized))));
        if (exactPath is not null)
        {
            return exactPath;
        }

        if (ReshadeState.IsStateFile(normalized))
        {
            return UniqueModKey(candidates.Where(document =>
                document.Files.Any(file =>
                    Path.GetFileName(file.CanonicalPath).Equals(ReshadeState.PrimaryIni, StringComparison.OrdinalIgnoreCase))));
        }

        var prefix = TryPackagePrefix(normalized);
        if (prefix is not null && IsPinStyleFile(normalized))
        {
            var folder = Path.GetFileName(prefix);
            var fromPrefix = UniqueModKey(candidates.Where(document =>
                document.Files.Any(file => GamePath.IsUnderOrEqual(file.CanonicalPath, prefix))
                || document.ModKey.Equals(folder, StringComparison.OrdinalIgnoreCase)));
            if (fromPrefix is not null)
            {
                return fromPrefix;
            }
        }

        if (!GamePath.IsUnderOrEqual(normalized, SptLayout.BepInExConfig)
            || !Path.GetExtension(normalized).Equals(".cfg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var guid = Path.GetFileNameWithoutExtension(normalized);
        if (string.IsNullOrWhiteSpace(guid))
        {
            return null;
        }

        var fromGuid = UniqueModKey(candidates.Where(document =>
            !string.IsNullOrWhiteSpace(document.ForgeGuid)
            && document.ForgeGuid.Equals(guid, StringComparison.OrdinalIgnoreCase)));
        if (fromGuid is not null)
        {
            return fromGuid;
        }

        return UniqueModKey(candidates.Where(document => MatchesPluginIdentity(guid, document)));
    }

    public static bool ShouldIgnore(string relativePath, SptOwnedBaseline? baseline)
    {
        var normalized = GamePath.Normalize(relativePath);
        if (SptDenylist.IsForbidden(normalized) || IsSecret(normalized) || IsProfileScoped(normalized))
        {
            return true;
        }

        return baseline is not null && baseline.RelativePaths.Contains(normalized);
    }

    private static string? TryPackagePrefix(string normalized)
    {
        foreach (var root in PackageRoots)
        {
            if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = normalized[root.Length..];
            var slash = rest.IndexOf('/');
            var folder = slash < 0 ? rest : rest[..slash];
            if (folder.Length > 0)
            {
                return root.TrimEnd('/') + "/" + folder;
            }
        }

        return null;
    }

    private static bool HasDirectoryNamed(string normalized, string name)
    {
        var parts = normalized.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDirectoryContaining(string normalized, string needle)
    {
        var parts = normalized.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPluginIdentity(string guid, ModDocument document)
    {
        foreach (var token in IdentityTokens(document))
        {
            if (IdentityEquals(guid, token))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> IdentityTokens(ModDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.ForgeGuid))
        {
            yield return document.ForgeGuid.Trim();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in document.Files)
        {
            var path = GamePath.Normalize(file.CanonicalPath);
            var prefix = TryPackagePrefix(path);
            if (prefix is not null)
            {
                var folder = Path.GetFileName(prefix);
                if (folder.Length >= 3 && seen.Add(folder))
                {
                    yield return folder;
                }
            }

            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dll = Path.GetFileNameWithoutExtension(path);
            if (dll.Length >= 3 && !SharedLibraryNames.Contains(dll) && seen.Add(dll))
            {
                yield return dll;
            }
        }
    }

    private static bool IdentityEquals(string guid, string token)
    {
        if (guid.Equals(token, StringComparison.OrdinalIgnoreCase)
            || Slug(guid).Equals(Slug(token), StringComparison.Ordinal))
        {
            return true;
        }

        var guidTokens = Tokenize(guid);
        var tokenParts = Tokenize(token);
        if (guidTokens.Length == 0 || tokenParts.Length == 0)
        {
            return false;
        }

        if (IsStrongTokenSet(tokenParts)
            && tokenParts.All(part => guidTokens.Contains(part, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        var guidLast = guidTokens[^1];
        var tokenLast = tokenParts[^1];
        return guidLast.Length >= 6
               && !WeakLastSegments.Contains(guidLast)
               && guidLast.Equals(tokenLast, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStrongTokenSet(string[] parts)
        => parts.Length >= 2 || (parts.Length == 1 && parts[0].Length >= 6);

    private static string[] Tokenize(string value)
        => value.Split(['.', '-', '_', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.ToLowerInvariant())
            .Where(part => part.Length > 0)
            .ToArray();

    private static string Slug(string value)
        => string.Concat(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));

    private static string? UniqueModKey(IEnumerable<ModDocument> matches)
    {
        string? key = null;
        foreach (var document in matches)
        {
            if (key is null)
            {
                key = document.ModKey;
                continue;
            }

            if (!key.Equals(document.ModKey, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return key;
    }
}
