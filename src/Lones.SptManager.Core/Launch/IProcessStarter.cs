namespace Lones.SptManager.Core.Launch;

public interface IProcessStarter
{
    StartedProcess Start(string exePath, string workingDirectory);
}

public sealed class ServerReadySnapshot
{
    public IReadOnlyDictionary<string, long> LogLengths { get; init; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
}

public interface IServerReadyProbe
{
    ServerReadySnapshot Snapshot(string gameRoot);
    bool WaitUntilReady(string gameRoot, TimeSpan timeout, ServerReadySnapshot snapshot);
}
