namespace Lones.SptManager.Core.Mapping;

public enum PackageKind
{
    Unknown = 0,
    Client = 1,
    Server = 2,
    Hybrid = 3,
    Tool = 4
}

public enum MapDisposition
{
    Mapped = 0,
    SkippedJunk = 1,
    SkippedSptData = 2,
    ToolNotMerged = 3,
    NeedsConfirm = 4,
    Forbidden = 5
}

public sealed record MappedEntry(
    string ArchivePath,
    string? CanonicalPath,
    MapDisposition Disposition,
    string? Note);

public sealed record PackageMap(
    PackageKind Kind,
    bool Deployable,
    bool NeedsConfirm,
    string? WrapperFolder,
    IReadOnlyList<MappedEntry> Entries,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<MappedEntry> DeployFiles
        => Entries.Where(entry => entry.Disposition == MapDisposition.Mapped && entry.CanonicalPath is not null).ToArray();
}

public sealed class MapperOptions
{
    public bool AllowSptData { get; init; }
    public bool AllowLowConfidence { get; init; }
    public bool ImportTools { get; init; }
    public long? ExpectedContentLength { get; init; }
    public string? ModKey { get; init; }
    public string? DisplayName { get; init; }
    public string? Version { get; init; }
    public int? ForgeModId { get; init; }
    public int? ForgeAddonId { get; init; }
    public string? ForgeGuid { get; init; }
    public string? ThumbnailUrl { get; init; }
}
