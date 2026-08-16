namespace Lones.SptManager.Core.Store;

public sealed class ModFileRecord
{
    public required string CanonicalPath { get; init; }
    public required string Sha256 { get; init; }
}

public sealed class ModDocument
{
    public int ManifestVersion { get; init; } = ProductInfo.ManifestVersion;
    public required string ModKey { get; init; }
    public string? DisplayName { get; init; }
    public required string Version { get; init; }
    public required string Kind { get; init; }
    public required bool Deployable { get; init; }
    public string? ArchiveHash { get; init; }
    public string? SourceArchive { get; init; }
    public int? ForgeModId { get; init; }
    public string? ForgeGuid { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? WrapperFolder { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<ModFileRecord> Files { get; init; } = [];
    public DateTimeOffset ImportedAtUtc { get; init; }
}
