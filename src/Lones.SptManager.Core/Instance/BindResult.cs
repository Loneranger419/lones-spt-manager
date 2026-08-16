using Lones.SptManager.Core.Paths;

namespace Lones.SptManager.Core.Instance;

public sealed record BindResult(
    BindStatus Status,
    string GameRoot,
    IReadOnlyList<string> MissingPaths,
    IReadOnlyList<BindWarning> Warnings,
    string? SptFileVersion,
    string? EftFileVersion,
    bool HasUserModsDirectory,
    bool HasUserProfilesDirectory,
    bool HasUserPatchersDirectory,
    bool HasUserLauncherConfig,
    string? GameRootVolumeId,
    string? Message)
{
    public bool IsSuccess => Status == BindStatus.Success;

    public static BindResult Fail(BindStatus status, string gameRoot, IEnumerable<string>? missing, string message)
        => new(
            status,
            gameRoot,
            missing?.Select(GamePath.Normalize).ToArray() ?? [],
            [],
            SptFileVersion: null,
            EftFileVersion: null,
            HasUserModsDirectory: false,
            HasUserProfilesDirectory: false,
            HasUserPatchersDirectory: false,
            HasUserLauncherConfig: false,
            GameRootVolumeId: null,
            message);
}
