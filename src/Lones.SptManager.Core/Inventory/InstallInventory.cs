using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Inventory;

public sealed class InventoryItem
{
    public required string Kind { get; init; }
    public required string Key { get; init; }
    public string? Version { get; init; }
    public bool Enabled { get; init; }
    public int Priority { get; init; }
    public required string Display { get; init; }
    public string? Note { get; init; }
    public string? InstallRelative { get; init; }
    public string? PackageKind { get; init; }
    public string? DisplayName { get; init; }
    public string? ThumbnailUrl { get; init; }
    public int? ForgeModId { get; init; }
    public int RuntimeFileCount { get; init; }
}

public sealed class InventorySnapshot
{
    public IReadOnlyList<InventoryItem> Items { get; init; } = [];
    public IReadOnlyList<OverlayConflict> Conflicts { get; init; } = [];
    public int LeftoverCount { get; init; }
}

public static class InstallInventory
{
    public const string StoreKind = "store";
    public const string LeftoverKind = "leftover";

    public static InventorySnapshot Scan(string? gameRoot, string managerData, string profileId)
    {
        profileId = ProfilePaths.Sanitize(profileId);
        var profile = new ProfileStore().TryRead(managerData, profileId);
        var enabled = profile?.Enabled ?? [];
        var explicitEnabled = profile is not null;
        var enabledLookup = enabled.ToDictionary(
            item => item.ModKey + "\0" + item.Version,
            StringComparer.OrdinalIgnoreCase);

        ProfileRuntimeStore.ImportLegacyStoreRuntime(managerData, profileId);
        var store = ModStore.List(managerData)
            .Where(document => document.Deployable)
            .OrderBy(document => document.ModKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(document => document.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var parentKeys = store
            .Where(document => !HarvestRules.IsRuntimeVersion(document.Version))
            .Select(document => document.ModKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = new List<InventoryItem>();
        foreach (var document in store)
        {
            if (HarvestRules.IsRuntimeVersion(document.Version) && parentKeys.Contains(document.ModKey))
            {
                continue;
            }

            var key = document.ModKey + "\0" + document.Version;
            var listed = enabledLookup.TryGetValue(key, out var row);
            var isOn = explicitEnabled ? listed && row!.IsOn : true;
            var priority = listed ? row!.Priority : int.MaxValue;
            var runtimeFiles = ProfileRuntimeStore.FileCount(managerData, profileId, document.ModKey);
            var displayVersion = runtimeFiles > 0 && !HarvestRules.IsRuntimeVersion(document.Version)
                ? $"{document.Version} + runtime"
                : document.Version;
            var title = string.IsNullOrWhiteSpace(document.DisplayName) ? document.ModKey : document.DisplayName;
            items.Add(new InventoryItem
            {
                Kind = StoreKind,
                Key = document.ModKey,
                Version = document.Version,
                Enabled = isOn,
                Priority = priority,
                Display = $"{(isOn ? "on" : "off")}  {priority}  {title} {displayVersion}  ({document.Kind})",
                Note = explicitEnabled ? null : "All store mods deploy until this profile saves an enabled list.",
                PackageKind = document.Kind,
                DisplayName = document.DisplayName,
                ThumbnailUrl = document.ThumbnailUrl,
                ForgeModId = document.ForgeModId,
                RuntimeFileCount = runtimeFiles
            });
        }

        var leftovers = ScanLeftovers(gameRoot, managerData, store);
        items.AddRange(leftovers);

        var conflicts = ReadConflicts(managerData, profileId);
        return new InventorySnapshot
        {
            Items = items,
            Conflicts = conflicts,
            LeftoverCount = leftovers.Count
        };
    }

    public static void SetEnabled(string managerData, string profileId, string modKey, string version, bool enabled)
        => UpsertLoadOrder(managerData, profileId, modKey, version, enabled);

    public static void AddToLoadOrder(string managerData, string profileId, string modKey, string version)
        => UpsertLoadOrder(managerData, profileId, modKey, version, enabled: true);

    public static void ReplaceLoadOrder(
        string managerData,
        string profileId,
        IReadOnlyList<(string ModKey, string Version)> order)
    {
        var store = new ProfileStore();
        var existing = store.TryRead(managerData, profileId);
        var enabled = new List<EnabledMod>(order.Count);
        for (var i = 0; i < order.Count; i++)
        {
            enabled.Add(new EnabledMod
            {
                ModKey = order[i].ModKey,
                Version = order[i].Version,
                Priority = i,
                Enabled = true
            });
        }

        SaveLoadOrder(store, managerData, profileId, enabled, existing);
    }

    public static void MovePriority(string managerData, string profileId, string modKey, string version, int delta)
    {
        var store = new ProfileStore();
        var existing = store.TryRead(managerData, profileId);
        var visible = (existing is null ? RuntimeAttachment.AllDeployable(managerData) : existing.Enabled.ToList())
            .Where(item => !HarvestRules.IsRuntimeVersion(item.Version))
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.ModKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var index = visible.FindIndex(item =>
            item.ModKey.Equals(modKey, StringComparison.OrdinalIgnoreCase)
            && item.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        var swap = index + delta;
        if (swap < 0 || swap >= visible.Count)
        {
            return;
        }

        (visible[index], visible[swap]) = (visible[swap], visible[index]);
        SaveLoadOrder(store, managerData, profileId, visible, existing);
    }

    public static void MoveTo(string managerData, string profileId, string modKey, string version, string targetKey, string targetVersion, bool after)
    {
        var store = new ProfileStore();
        var existing = store.TryRead(managerData, profileId);
        var visible = (existing is null ? RuntimeAttachment.AllDeployable(managerData) : existing.Enabled.ToList())
            .Where(item => !HarvestRules.IsRuntimeVersion(item.Version))
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.ModKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var from = visible.FindIndex(item =>
            item.ModKey.Equals(modKey, StringComparison.OrdinalIgnoreCase)
            && item.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        var to = visible.FindIndex(item =>
            item.ModKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase)
            && item.Version.Equals(targetVersion, StringComparison.OrdinalIgnoreCase));
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        var row = visible[from];
        visible.RemoveAt(from);
        if (from < to)
        {
            to--;
        }

        if (after)
        {
            to++;
        }

        to = Math.Clamp(to, 0, visible.Count);
        visible.Insert(to, row);
        SaveLoadOrder(store, managerData, profileId, visible, existing);
    }

    private static void SaveLoadOrder(
        ProfileStore store,
        string managerData,
        string profileId,
        IEnumerable<EnabledMod> order,
        ProfileDocument? existing)
        => store.Save(
            managerData,
            profileId,
            RuntimeAttachment.WithoutStoreRuntime(order),
            existing?.LaunchMode,
            existing?.JoinUrl);

    private static void UpsertLoadOrder(
        string managerData,
        string profileId,
        string modKey,
        string version,
        bool enabled)
    {
        var store = new ProfileStore();
        var existing = store.TryRead(managerData, profileId);
        var current = existing is null
            ? RuntimeAttachment.AllDeployable(managerData)
            : existing.Enabled.ToList();
        var index = current.FindIndex(item =>
            item.ModKey.Equals(modKey, StringComparison.OrdinalIgnoreCase)
            && item.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            var row = current[index];
            current[index] = new EnabledMod
            {
                ModKey = row.ModKey,
                Version = row.Version,
                Priority = row.Priority,
                Enabled = enabled
            };
        }
        else
        {
            current.Add(new EnabledMod
            {
                ModKey = modKey,
                Version = version,
                Priority = current.Count == 0 ? 0 : current.Max(item => item.Priority) + 1,
                Enabled = enabled
            });
        }

        store.Save(
            managerData,
            profileId,
            RuntimeAttachment.WithoutStoreRuntime(
                current.OrderBy(item => item.Priority).ThenBy(item => item.ModKey, StringComparer.OrdinalIgnoreCase)),
            existing?.LaunchMode,
            existing?.JoinUrl);
    }

    private static List<InventoryItem> ScanLeftovers(string? gameRoot, string managerData, IReadOnlyList<ModDocument> store)
    {
        var leftovers = new List<InventoryItem>();
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return leftovers;
        }

        var profilesRoot = ProfilePaths.ProfilesRoot(managerData);
        var managedNames = store
            .SelectMany(document => document.Files.Select(file => FolderHint(file.CanonicalPath)))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ScanChildren(
            leftovers,
            GamePath.Combine(gameRoot, OverlayPlanner.BepInExPlugins),
            OverlayPlanner.BepInExPlugins,
            profilesRoot,
            managedNames,
            skip: "spt");
        var mods = GamePath.Combine(gameRoot, SptLayout.UserMods);
        if (Directory.Exists(mods) && !IsOwnedJunction(mods, profilesRoot))
        {
            ScanChildren(leftovers, mods, SptLayout.UserMods, profilesRoot, managedNames, skip: null);
        }

        return leftovers;
    }

    private static void ScanChildren(
        List<InventoryItem> leftovers,
        string directory,
        string parentRelative,
        string profilesRoot,
        IReadOnlySet<string> managedNames,
        string? skip)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (skip is not null && name.Equals(skip, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsOwnedJunction(child, profilesRoot) || managedNames.Contains(name))
            {
                continue;
            }

            var relative = parentRelative + "/" + name;
            leftovers.Add(new InventoryItem
            {
                Kind = LeftoverKind,
                Key = name,
                Enabled = false,
                Display = $"disk  {relative}  (in install, not in store)",
                Note = "Select Import leftover to copy this folder into the store, then Deploy.",
                InstallRelative = relative,
                PackageKind = LeftoverKind
            });
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(file);
            var relative = parentRelative + "/" + name;
            leftovers.Add(new InventoryItem
            {
                Kind = LeftoverKind,
                Key = name,
                Enabled = false,
                Display = $"disk  {relative}  (loose file in install)",
                Note = "Select Import leftover to copy this file into the store, then Deploy.",
                InstallRelative = relative,
                PackageKind = LeftoverKind
            });
        }
    }

    private static bool IsOwnedJunction(string path, string profilesRoot)
    {
        if (!NtfsLinks.IsJunction(path))
        {
            return false;
        }

        var target = NtfsLinks.TryGetJunctionTarget(path);
        return target is not null
               && (SafeFileSystem.IsUnderDirectory(target, profilesRoot) || SafeFileSystem.SamePath(target, profilesRoot));
    }

    private static string? FolderHint(string canonical)
    {
        if (GamePath.IsUnderOrEqual(canonical, OverlayPlanner.BepInExPlugins))
        {
            var rest = canonical[OverlayPlanner.BepInExPlugins.Length..].Trim('/');
            return rest.Split('/')[0];
        }

        if (GamePath.IsUnderOrEqual(canonical, SptLayout.UserMods))
        {
            var rest = canonical[(SptLayout.UserMods.Length + 1)..];
            return rest.Split('/')[0];
        }

        return null;
    }

    private static IReadOnlyList<OverlayConflict> ReadConflicts(string managerData, string profileId)
    {
        var path = ProfilePaths.Manifest(managerData, profileId);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var manifest = System.Text.Json.JsonSerializer.Deserialize<DeployManifest>(File.ReadAllText(path), ProfileStore.JsonOptions);
            return manifest?.Conflicts ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
