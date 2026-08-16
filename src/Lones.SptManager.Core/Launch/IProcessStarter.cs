namespace Lones.SptManager.Core.Launch;

public interface IProcessStarter
{
    StartedProcess Start(string exePath, string workingDirectory);
}

public interface IServerReadyProbe
{
    bool WaitUntilReady(string gameRoot, TimeSpan timeout);
}
