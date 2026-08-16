using Lones.SptManager.Core.Paths;

namespace Lones.SptManager.Core.Instance;

public static class SptDenylist
{
    private static readonly string[] Prefixes =
    [
        SptLayout.BepInExPluginsSpt,
        SptLayout.BepInExCore,
        SptLayout.SptData,
        SptLayout.WinHttpDll,
        SptLayout.DoorstopConfig,
        SptLayout.DoorstopVersion,
        SptLayout.SptPrepatchDll,
        SptLayout.SptServerExe,
        SptLayout.SptLauncherExe
    ];

    public static bool IsForbidden(string relativePath)
    {
        var normalized = GamePath.Normalize(relativePath);
        if (Prefixes.Any(prefix => GamePath.IsUnderOrEqual(normalized, prefix)))
        {
            return true;
        }

        // SPT_Runtime binaries live next to the server, never under user/.
        if (normalized.StartsWith(SptLayout.SptRuntime + "/", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith(SptLayout.SptRuntime + "/user/", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = normalized[(SptLayout.SptRuntime.Length + 1)..];
            if (!remainder.Contains('/', StringComparison.Ordinal)
                && (remainder.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || remainder.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
