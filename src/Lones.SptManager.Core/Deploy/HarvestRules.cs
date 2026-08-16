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

    public static string? TryOwnedModKey(string relativePath)
        => TryOwnedModKey(relativePath, store: null);

    public static string? TryOwnedModKey(string relativePath, IReadOnlyList<ModDocument>? store)
    {
        var normalized = GamePath.Normalize(relativePath);
        var fikaOwned = FikaServerOwnedFiles.Contains(normalized);
        if (!fikaOwned && !ShouldPinToMod(normalized))
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
        var prefix = TryPackagePrefix(normalized);
        foreach (var document in store)
        {
            if (!document.Deployable || IsRuntimeVersion(document.Version))
            {
                continue;
            }

            if (prefix is not null
                && document.Files.Any(file => GamePath.IsUnderOrEqual(file.CanonicalPath, prefix)))
            {
                return document.ModKey;
            }

            if (prefix is not null
                && document.ModKey.Equals(Path.GetFileName(prefix), StringComparison.OrdinalIgnoreCase))
            {
                return document.ModKey;
            }
        }

        if (GamePath.IsUnderOrEqual(normalized, SptLayout.BepInExConfig))
        {
            var guid = Path.GetFileNameWithoutExtension(normalized);
            foreach (var document in store)
            {
                if (document.Deployable
                    && !IsRuntimeVersion(document.Version)
                    && !string.IsNullOrWhiteSpace(document.ForgeGuid)
                    && document.ForgeGuid.Equals(guid, StringComparison.OrdinalIgnoreCase))
                {
                    return document.ModKey;
                }
            }
        }

        return null;
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
}
