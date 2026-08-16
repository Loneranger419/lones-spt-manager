using Lones.SptManager.Core;

namespace Lones.SptManager.Forge;

public static class ForgeRestrictedMods
{
    public const int SptModManagerId = 2851;
    public const string SptModManagerSlug = "spt-mod-manager";
    public const string SptModManagerGuid = "com.nevek20.sptmodmanager";
    public const string SptModManagerName = "SPT Mod Manager";

    public static string Reason(string? displayName = null)
    {
        var label = string.IsNullOrWhiteSpace(displayName) ? SptModManagerName : displayName.Trim();
        return label + " is incompatible with " + ProductInfo.Name + " and cannot be downloaded from The Forge.";
    }

    public static bool IsRestricted(int? id = null, string? guid = null, string? slug = null, string? name = null)
        => id == SptModManagerId
           || EqualsIgnore(guid, SptModManagerGuid)
           || EqualsIgnore(slug, SptModManagerSlug)
           || EqualsIgnore(name, SptModManagerName);

    public static bool IsRestricted(ForgeMod mod)
        => IsRestricted(mod.Id, mod.Guid, mod.Slug, mod.Name);

    public static bool IsRestricted(ForgeSearchHit hit)
        => IsRestricted(hit.ModId, hit.Guid, hit.Slug, hit.Name);

    public static bool IsRestricted(ModPackEntry entry)
        => IsRestricted(entry.Id, guid: null, entry.Slug, entry.Name);

    public static bool IsRestricted(ForgeDependencyNode node)
        => IsRestricted(node.Id, node.Guid, node.Slug, node.Name);

    private static bool EqualsIgnore(string? value, string expected)
        => !string.IsNullOrWhiteSpace(value)
           && string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
