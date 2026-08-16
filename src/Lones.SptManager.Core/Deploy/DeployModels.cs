namespace Lones.SptManager.Core.Deploy;

public sealed class EnabledMod
{
    public required string ModKey { get; init; }
    public required string Version { get; init; }
    public int Priority { get; init; }
    public bool? Enabled { get; init; }

    public bool IsOn => Enabled != false;
}

public sealed class JunctionRecord
{
    public required string InstallRelative { get; init; }
    public required string StagingRelative { get; init; }
    public required string TargetFull { get; init; }
}

public sealed class CopiedFileRecord
{
    public required string InstallRelative { get; init; }
    public required string Sha256 { get; init; }
}

public sealed class StagedFileRecord
{
    public required string CanonicalPath { get; init; }
    public required string Sha256 { get; init; }
    public required string WinnerModKey { get; init; }
}

public sealed class OverlayConflict
{
    public required string CanonicalPath { get; init; }
    public required string WinnerModKey { get; init; }
    public required string LoserModKey { get; init; }
}

public sealed class DeployManifest
{
    public int ManifestVersion { get; init; } = ProductInfo.ManifestVersion;
    public required string ProfileId { get; init; }
    public required string GameRoot { get; init; }
    public required string Fingerprint { get; init; }
    public DateTimeOffset WrittenAtUtc { get; init; }
    public IReadOnlyList<EnabledMod> Enabled { get; init; } = [];
    public IReadOnlyList<JunctionRecord> Junctions { get; init; } = [];
    public IReadOnlyList<CopiedFileRecord> CopiedFiles { get; init; } = [];
    public IReadOnlyList<StagedFileRecord> StagedFiles { get; init; } = [];
    public IReadOnlyList<OverlayConflict> Conflicts { get; init; } = [];
}

public sealed class DeployRequest
{
    public required string GameRoot { get; init; }
    public required string ManagerData { get; init; }
    public string ProfileId { get; init; } = Profiles.ProfilePaths.DefaultProfileId;
    public IReadOnlyList<EnabledMod>? Enabled { get; init; }
    public Instance.SptOwnedBaseline? Baseline { get; init; }
}

public enum DeployStatus
{
    Success = 0,
    Idempotent = 1,
    BlockedProcesses = 2,
    Failed = 3,
    Recovered = 4,
    Clean = 5
}

public sealed class DeployResult
{
    public required DeployStatus Status { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> RunningProcesses { get; init; } = [];
    public IReadOnlyList<JunctionRecord> Junctions { get; init; } = [];
    public IReadOnlyList<OverlayConflict> Conflicts { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
