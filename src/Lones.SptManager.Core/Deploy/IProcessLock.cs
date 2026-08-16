using System.Diagnostics;

namespace Lones.SptManager.Core.Deploy;

public interface IProcessLock
{
    IReadOnlyList<string> RunningSptProcesses();
}

public sealed class SptProcessLock : IProcessLock
{
    public static readonly string[] WatchedNames = ["SPT.Server", "SPT.Launcher", "EscapeFromTarkov"];

    public IReadOnlyList<string> RunningSptProcesses()
    {
        var running = new List<string>();
        foreach (var name in WatchedNames)
        {
            if (Process.GetProcessesByName(name).Length > 0)
            {
                running.Add(name);
            }
        }

        return running;
    }
}
