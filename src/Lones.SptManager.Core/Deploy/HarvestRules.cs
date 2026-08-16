using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;

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

    public static string? TryOwnedModKey(string relativePath)
    {
        var normalized = GamePath.Normalize(relativePath);
        if (FikaServerOwnedFiles.Contains(normalized))
        {
            return "fika-server";
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
}
