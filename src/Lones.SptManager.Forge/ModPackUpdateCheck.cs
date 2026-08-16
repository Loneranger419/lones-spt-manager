using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Forge;

public sealed class ModPackUpdateChange
{
    public required string DisplayName { get; init; }
    public string? CurrentVersion { get; init; }
    public string? PackVersion { get; init; }
    public required string Kind { get; init; }
}

public sealed class ModPackUpdateReport
{
    public IReadOnlyList<ModPackUpdateChange> Changes { get; init; } = [];

    public bool HasUpdates => Changes.Count > 0;

    public string Summary
    {
        get
        {
            if (Changes.Count == 0)
            {
                return string.Empty;
            }

            var newer = Changes.Count(item => item.Kind == NewerVersion);
            var added = Changes.Count(item => item.Kind == NewMod);
            var parts = new List<string>();
            if (newer > 0)
            {
                parts.Add(newer == 1 ? "1 newer version" : newer + " newer versions");
            }

            if (added > 0)
            {
                parts.Add(added == 1 ? "1 new mod" : added + " new mods");
            }

            return "Pack update: " + string.Join(", ", parts) + ". Edit the profile to install.";
        }
    }

    public const string NewerVersion = "newer";
    public const string NewMod = "new";
}

public static class ModPackUpdateCheck
{
    public static ModPackUpdateReport Compare(
        ModPackManifest pack,
        IReadOnlyList<EnabledMod> enabled,
        IReadOnlyList<ModDocument> store)
    {
        var onProfile = enabled
            .Where(item => item.IsOn)
            .ToArray();
        var changes = new List<ModPackUpdateChange>();
        foreach (var entry in pack.ListedMods())
        {
            if (ForgeRestrictedMods.IsRestricted(entry))
            {
                continue;
            }

            var current = TryEnabledVersion(entry.Id, onProfile, store);
            if (current is null)
            {
                changes.Add(new ModPackUpdateChange
                {
                    DisplayName = entry.DisplayName,
                    PackVersion = entry.RequestedVersion,
                    Kind = ModPackUpdateReport.NewMod
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.RequestedVersion)
                || ModPackInstaller.VersionEquals(current, entry.RequestedVersion))
            {
                continue;
            }

            var order = CompareVersions(entry.RequestedVersion, current);
            if (order is < 0)
            {
                continue;
            }

            changes.Add(new ModPackUpdateChange
            {
                DisplayName = entry.DisplayName,
                CurrentVersion = current,
                PackVersion = entry.RequestedVersion,
                Kind = ModPackUpdateReport.NewerVersion
            });
        }

        return new ModPackUpdateReport { Changes = changes };
    }

    public static int? CompareVersions(string? left, string? right)
    {
        var a = NormalizeVersion(left);
        var b = NormalizeVersion(right);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return null;
        }

        var leftParts = a.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightParts = b.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = Math.Max(leftParts.Length, rightParts.Length);
        for (var i = 0; i < count; i++)
        {
            var leftPart = i < leftParts.Length ? leftParts[i] : "0";
            var rightPart = i < rightParts.Length ? rightParts[i] : "0";
            var leftNumber = TryNumericPrefix(leftPart, out var leftRest);
            var rightNumber = TryNumericPrefix(rightPart, out var rightRest);
            if (leftNumber is not null && rightNumber is not null)
            {
                var number = leftNumber.Value.CompareTo(rightNumber.Value);
                if (number != 0)
                {
                    return number;
                }

                var rest = string.Compare(leftRest, rightRest, StringComparison.OrdinalIgnoreCase);
                if (rest != 0)
                {
                    return rest;
                }

                continue;
            }

            var text = string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);
            if (text != 0)
            {
                return text;
            }
        }

        return 0;
    }

    private static string? TryEnabledVersion(
        int forgeModId,
        IReadOnlyList<EnabledMod> enabled,
        IReadOnlyList<ModDocument> store)
    {
        var keys = store
            .Where(document => document.Deployable && document.ForgeModId == forgeModId)
            .Select(document => document.ModKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return enabled
            .Where(item => keys.Contains(item.ModKey))
            .Select(item => item.Version)
            .FirstOrDefault();
    }

    private static string NormalizeVersion(string? version)
    {
        var value = (version ?? "").Trim();
        if (value.Length >= 2 && (value[0] is 'v' or 'V') && char.IsDigit(value[1]))
        {
            return value[1..];
        }

        return value;
    }

    private static int? TryNumericPrefix(string value, out string rest)
    {
        var i = 0;
        while (i < value.Length && char.IsDigit(value[i]))
        {
            i++;
        }

        if (i == 0 || !int.TryParse(value[..i], out var number))
        {
            rest = value;
            return null;
        }

        rest = value[i..];
        return number;
    }
}
