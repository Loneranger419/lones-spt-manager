using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Launch;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;

namespace Lones.SptManager.Tests;

public sealed class LaunchEngineTests
{
    [Fact]
    public void Solo_StartsServerThenLauncher()
    {
        using var fx = new LaunchFixture();
        var result = fx.Engine.Launch(fx.Request(LaunchModes.Solo));
        Assert.True(result.Success, result.Message);
        Assert.Equal(["SPT.Server", "SPT.Launcher"], fx.Starter.Names);
        Assert.True(result.StartedServer);
        Assert.All(fx.Starter.Started, item => Assert.Equal(fx.Runtime, item.WorkingDirectory));
    }

    [Fact]
    public void FikaHost_StartsServerThenLauncher_AndWarnsUdp()
    {
        using var fx = new LaunchFixture();
        var result = fx.Engine.Launch(fx.Request(LaunchModes.FikaHost));
        Assert.True(result.Success, result.Message);
        Assert.Equal(["SPT.Server", "SPT.Launcher"], fx.Starter.Names);
        Assert.Contains(result.Warnings, text => text.Contains("25565"));
        Assert.Contains(result.Warnings, text => text.Contains("fika-server"));
    }

    [Fact]
    public void FikaClient_DoesNotStartServer_PatchesJoinUrl()
    {
        using var fx = new LaunchFixture();
        File.WriteAllText(fx.Install(SptLayout.UserLauncherConfig), """{"Name":"keep-me","Url":"https://old:6969"}""");
        var result = fx.Engine.Launch(fx.Request(LaunchModes.FikaClient, "join.example:6969"));
        Assert.True(result.Success, result.Message);
        Assert.Equal(["SPT.Launcher"], fx.Starter.Names);
        Assert.False(result.StartedServer);
        Assert.Equal("https://join.example:6969", result.AppliedJoinUrl);
        var json = File.ReadAllText(fx.Install(SptLayout.UserLauncherConfig));
        Assert.Contains("https://join.example:6969", json);
        Assert.Contains("keep-me", json);
        Assert.DoesNotContain("SPT.Server", fx.Starter.Names);
    }

    [Fact]
    public void FikaClient_MissingUrl_Fails()
    {
        using var fx = new LaunchFixture();
        var result = fx.Engine.Launch(fx.Request(LaunchModes.FikaClient));
        Assert.False(result.Success);
        Assert.Contains("URL", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fx.Starter.Started);
    }

    [Fact]
    public void Launch_BlockedWhileSptRunning()
    {
        using var fx = new LaunchFixture();
        fx.Lock.Running.Add("SPT.Launcher");
        var result = fx.Engine.Launch(fx.Request(LaunchModes.Solo));
        Assert.False(result.Success);
        Assert.Contains("blocked", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fx.Starter.Started);
    }

    [Fact]
    public void ServerNotReady_DoesNotStartLauncher()
    {
        using var fx = new LaunchFixture();
        fx.Ready.Ready = false;
        var result = fx.Engine.Launch(fx.Request(LaunchModes.Solo));
        Assert.False(result.Success);
        Assert.Equal(["SPT.Server"], fx.Starter.Names);
    }

    [Fact]
    public void NormalizeJoinUrl_RequiresHttpsAndPort()
    {
        Assert.Equal("https://10.0.0.2:6969", LauncherUrlPatcher.NormalizeJoinUrl("10.0.0.2"));
        Assert.Equal("https://host.example:6969", LauncherUrlPatcher.NormalizeJoinUrl("https://host.example:6969/"));
        Assert.Throws<InvalidOperationException>(() => LauncherUrlPatcher.NormalizeJoinUrl("http://host:6969"));
    }

    [Fact]
    public void ProcessStarter_RefusesLiveEftAndBattlEye()
    {
        var starter = new ProcessStarter();
        Assert.Throws<InvalidOperationException>(() => starter.Start(@"C:\game\EscapeFromTarkov.exe", @"C:\game"));
        Assert.Throws<InvalidOperationException>(() => starter.Start(@"C:\game\BattlEye\BEService.exe", @"C:\game"));
    }
}

internal sealed class RecordingStarter : IProcessStarter
{
    public List<StartedProcess> Started { get; } = [];

    public IReadOnlyList<string> Names => Started.Select(item => item.Name).ToArray();

    public StartedProcess Start(string exePath, string workingDirectory)
    {
        var started = new StartedProcess
        {
            Name = Path.GetFileNameWithoutExtension(exePath),
            ExePath = exePath,
            WorkingDirectory = workingDirectory,
            Id = Started.Count + 1
        };
        Started.Add(started);
        return started;
    }
}

internal sealed class StubReadyProbe : IServerReadyProbe
{
    public bool Ready { get; set; } = true;

    public bool WaitUntilReady(string gameRoot, TimeSpan timeout) => Ready;
}

internal sealed class LaunchFixture : IDisposable
{
    public string Root { get; }
    public string GameRoot { get; }
    public string ManagerData { get; }
    public string Runtime => GamePath.Combine(GameRoot, SptLayout.SptRuntime);
    public RecordingStarter Starter { get; } = new();
    public StubReadyProbe Ready { get; } = new();
    public StubProcessLock Lock { get; } = new();
    public LaunchEngine Engine { get; }

    public LaunchFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "lones-launch-" + Guid.NewGuid().ToString("N"));
        GameRoot = Path.Combine(Root, "game");
        ManagerData = Path.Combine(Root, "manager");
        Directory.CreateDirectory(ManagerData);
        GameRootFixture.Create41Layout(GameRoot);
        Engine = new LaunchEngine(Starter, Ready, Lock);
    }

    public LaunchRequest Request(string mode, string? joinUrl = null)
        => new()
        {
            GameRoot = GameRoot,
            ManagerData = ManagerData,
            Mode = mode,
            JoinUrl = joinUrl,
            ReadyTimeout = TimeSpan.FromMilliseconds(1)
        };

    public string Install(string relative)
    {
        var path = GamePath.Combine(GameRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
