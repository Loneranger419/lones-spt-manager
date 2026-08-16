using System.Text.Json.Serialization;

namespace Lones.SptManager.Forge;

public sealed class ForgeEnvelope<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
}

public sealed class ForgeMod
{
    public int Id { get; set; }
    public string? Guid { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Teaser { get; set; }
    public string? Thumbnail { get; set; }
    public string? DetailUrl { get; set; }
    public bool? FikaCompatibility { get; set; }
    public List<ForgeVersion> Versions { get; set; } = [];
}

public sealed class ForgeVersion
{
    public int Id { get; set; }
    public string? Version { get; set; }
    public string? Link { get; set; }
    public long? ContentLength { get; set; }
    public string? SptVersionConstraint { get; set; }
    public string? FikaCompatibility { get; set; }
}

public sealed class ForgeDependencyNode
{
    public int? Id { get; set; }
    public string? Guid { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public bool Conflict { get; set; }
    public ForgeVersion? LatestCompatibleVersion { get; set; }
    public List<ForgeDependencyNode> Dependencies { get; set; } = [];
}

public sealed class ForgeUpdates
{
    public string? SptVersion { get; set; }
    public List<ForgeUpdateOffer> Updates { get; set; } = [];
    public List<ForgeBlockedUpdate> BlockedUpdates { get; set; } = [];
    public List<ForgeInstalledRef> UpToDate { get; set; } = [];
    public List<ForgeInstalledRef> IncompatibleWithSpt { get; set; } = [];
}

public sealed class ForgeUpdateOffer
{
    public ForgeInstalledRef? CurrentVersion { get; set; }
    public ForgeVersion? RecommendedVersion { get; set; }
    public string? UpdateReason { get; set; }
}

public sealed class ForgeBlockedUpdate
{
    public ForgeInstalledRef? CurrentVersion { get; set; }
    public string? Reason { get; set; }
    public string? BlockedReason { get; set; }
}

public sealed class ForgeInstalledRef
{
    public int? Id { get; set; }
    public int? ModId { get; set; }
    public string? Guid { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Version { get; set; }
}

public sealed class ForgeAddon
{
    public int Id { get; set; }
    public int? ModId { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public List<ForgeVersion> Versions { get; set; } = [];
}

public sealed class DownloadProgress
{
    public long Bytes { get; init; }
    public long? Total { get; init; }

    public string Display
    {
        get
        {
            var got = FormatBytes(Bytes);
            return Total is > 0 ? $"{got} / {FormatBytes(Total.Value)}" : got;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }

        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024.0).ToString("0.0") + " KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
        }

        return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00") + " GB";
    }
}

public sealed class ForgeSearchHit
{
    public required int ModId { get; init; }
    public string? Guid { get; init; }
    public required string Name { get; init; }
    public string? Slug { get; init; }
    public string? Teaser { get; init; }
    public string? Thumbnail { get; init; }
    public string? Version { get; init; }
    public string? DownloadLink { get; init; }
    public long? ContentLength { get; init; }
    public string? SptVersionConstraint { get; init; }
    public string? VersionFikaCompatibility { get; init; }
    public bool? ModFikaCompatibility { get; init; }

    public string Display => $"{Name} ({ModId}) {(Version is null ? "" : "— " + Version)}".Trim();

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : char.ToUpperInvariant(Name.Trim()[0]).ToString();
}
