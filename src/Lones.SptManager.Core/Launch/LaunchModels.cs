namespace Lones.SptManager.Core.Launch;

public static class LaunchModes
{
    public const string Solo = "solo";
    public const string FikaHost = "fika-host";
    public const string FikaClient = "fika-client";

    public static bool StartsServer(string mode)
        => mode is Solo or FikaHost;
}

public sealed class LaunchRequest
{
    public required string GameRoot { get; init; }
    public required string ManagerData { get; init; }
    public string ProfileId { get; init; } = Profiles.ProfilePaths.DefaultProfileId;
    public string Mode { get; init; } = LaunchModes.Solo;
    public string? JoinUrl { get; init; }
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed class StartedProcess
{
    public required string Name { get; init; }
    public required string ExePath { get; init; }
    public required string WorkingDirectory { get; init; }
    public int? Id { get; init; }
}

public sealed class LaunchResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<StartedProcess> Started { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? AppliedJoinUrl { get; init; }
    public bool StartedServer { get; init; }
}
