using System.Security.Cryptography;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Deploy;

/// <summary>
/// ReShade writes <c>ReShade2.ini</c> when <c>ReShade.ini</c> is not writable (common after a
/// store copy). Deploy then restores the pack ini and the tutorial runs again.
/// </summary>
public static class ReshadeState
{
    public const string PrimaryIni = "ReShade.ini";

    public static bool IsStateFile(string relativePath)
    {
        var name = Path.GetFileName(GamePath.Normalize(relativePath));
        return name.Equals(PrimaryIni, StringComparison.OrdinalIgnoreCase)
               || name.Equals("ReShadePreset.ini", StringComparison.OrdinalIgnoreCase)
               || IsSidecarIni(name);
    }

    public static bool IsSidecarIni(string fileName)
    {
        if (!fileName.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var mid = fileName["ReShade".Length..^".ini".Length];
        return mid.Length > 0 && mid.All(char.IsDigit);
    }

    public static string? PromoteSidecars(string gameRoot)
    {
        var primary = GamePath.Combine(gameRoot, PrimaryIni);
        var best = BestIni(gameRoot);
        if (best is null)
        {
            return null;
        }

        if (!SafeFileSystem.SamePath(best, primary))
        {
            File.Copy(best, primary, overwrite: true);
        }

        ClearReadOnly(primary);
        return primary;
    }

    public static void CaptureToProfile(string gameRoot, string managerData, string profileId)
    {
        var best = BestIni(gameRoot);
        if (best is null || ReadTutorialProgress(best) <= 0)
        {
            return;
        }

        var primary = PromoteSidecars(gameRoot);
        if (primary is null || !File.Exists(primary))
        {
            return;
        }

        var owner = HarvestRules.TryOwnedModKey(PrimaryIni, ModStore.List(managerData).ToArray());
        if (owner is null)
        {
            return;
        }

        ProfileRuntimeStore.UpsertFile(managerData, profileId, owner, PrimaryIni, primary, HashFile(primary));
    }

    public static void ClearReadOnly(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.IsReadOnly)
        {
            info.IsReadOnly = false;
        }
    }

    public static int ReadTutorialProgress(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("TutorialProgress", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq > 0 && int.TryParse(trimmed[(eq + 1)..].Trim(), out var value))
            {
                return value;
            }
        }

        return 0;
    }

    private static string? BestIni(string gameRoot)
    {
        if (!Directory.Exists(gameRoot))
        {
            return null;
        }

        string? best = null;
        var bestProgress = -1;
        foreach (var file in Directory.EnumerateFiles(gameRoot, "ReShade*.ini"))
        {
            var name = Path.GetFileName(file);
            if (!name.Equals(PrimaryIni, StringComparison.OrdinalIgnoreCase) && !IsSidecarIni(name))
            {
                continue;
            }

            var progress = ReadTutorialProgress(file);
            if (best is null || progress > bestProgress)
            {
                best = file;
                bestProgress = progress;
            }
        }

        return best;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
