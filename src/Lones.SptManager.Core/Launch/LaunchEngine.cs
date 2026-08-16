using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Core.Launch;

public sealed class LaunchEngine
{
    private readonly IProcessStarter _starter;
    private readonly IServerReadyProbe _ready;
    private readonly IProcessLock _lock;
    private readonly ProfileStore _profiles = new();

    public LaunchEngine()
        : this(new ProcessStarter(), new TcpOrLogReadyProbe(), new SptProcessLock())
    {
    }

    public LaunchEngine(IProcessStarter starter, IServerReadyProbe ready, IProcessLock processLock)
    {
        _starter = starter;
        _ready = ready;
        _lock = processLock;
    }

    public LaunchResult Launch(LaunchRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameRoot);
        var gameRoot = Path.GetFullPath(request.GameRoot);
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? LaunchModes.Solo : request.Mode.Trim().ToLowerInvariant();
        var warnings = new List<string>();
        var started = new List<StartedProcess>();

        if (!File.Exists(GamePath.Combine(gameRoot, SptLayout.SptServerExe))
            || !File.Exists(GamePath.Combine(gameRoot, SptLayout.SptLauncherExe)))
        {
            return Fail("Game root is missing SPT.Server.exe or SPT.Launcher.exe under SPT_Runtime.");
        }

        var running = _lock.RunningSptProcesses();
        if (running.Count > 0)
        {
            return Fail("Launch blocked while SPT processes are running: " + string.Join(", ", running));
        }

        if (mode is not LaunchModes.Solo and not LaunchModes.FikaHost and not LaunchModes.FikaClient)
        {
            return Fail("Unknown launch mode: " + request.Mode);
        }

        if (mode is LaunchModes.FikaHost or LaunchModes.FikaClient)
        {
            warnings.AddRange(FikaWarnings(gameRoot, request.ManagerData, request.ProfileId, mode));
        }

        string? appliedUrl = null;
        if (mode == LaunchModes.FikaClient)
        {
            if (string.IsNullOrWhiteSpace(request.JoinUrl))
            {
                return Fail("Fika join needs a host URL (https://host:6969).");
            }

            try
            {
                appliedUrl = LauncherUrlPatcher.Apply(gameRoot, request.JoinUrl);
                warnings.Add(
                    "Set SPT.Launcher → Settings → Developer Mode to this URL if the launcher does not pick up user\\launcher\\config.json: "
                    + appliedUrl);
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        var runtime = GamePath.Combine(gameRoot, SptLayout.SptRuntime);
        if (LaunchModes.StartsServer(mode))
        {
            started.Add(_starter.Start(GamePath.Combine(gameRoot, SptLayout.SptServerExe), runtime));
            if (!_ready.WaitUntilReady(gameRoot, request.ReadyTimeout))
            {
                return new LaunchResult
                {
                    Success = false,
                    Message = "SPT.Server did not become ready (TCP 6969 or 'Server has started' in logs).",
                    Started = started,
                    Warnings = warnings,
                    StartedServer = true
                };
            }
        }

        started.Add(_starter.Start(GamePath.Combine(gameRoot, SptLayout.SptLauncherExe), runtime));

        if (!string.IsNullOrWhiteSpace(request.ManagerData))
        {
            var profile = _profiles.LoadOrCreate(request.ManagerData, request.ProfileId);
            _profiles.Save(request.ManagerData, request.ProfileId, profile.Enabled, mode, appliedUrl ?? request.JoinUrl);
        }

        if (mode == LaunchModes.FikaHost)
        {
            warnings.Add("Fika host: forward TCP 6969 for the backend. UDP 25565 is the raid-hosting game client, not necessarily this PC.");
        }

        return new LaunchResult
        {
            Success = true,
            Message = Summarize(mode, started, appliedUrl),
            Started = started,
            Warnings = warnings,
            AppliedJoinUrl = appliedUrl,
            StartedServer = started.Any(item => item.Name.Equals("SPT.Server", StringComparison.OrdinalIgnoreCase))
        };
    }

    public HarvestResult WaitThenHarvest(
        LaunchResult launch,
        string gameRoot,
        string managerData,
        string profileId,
        Func<IReadOnlyList<StartedProcess>, bool>? waitUntilDone = null)
    {
        if (waitUntilDone is not null)
        {
            waitUntilDone(launch.Started);
        }

        return new HarvestEngine(_lock).Harvest(gameRoot, managerData, profileId);
    }

    private static IReadOnlyList<string> FikaWarnings(string gameRoot, string managerData, string profileId, string mode)
    {
        var warnings = new List<string>();
        var hasPlugin = Directory.Exists(GamePath.Combine(gameRoot, "BepInEx/plugins/Fika"));
        var hasServer = Directory.Exists(GamePath.Combine(gameRoot, SptLayout.UserMods + "/fika-server"));
        if (!hasPlugin)
        {
            var staging = ProfilePaths.Staging(managerData, profileId);
            hasPlugin = Directory.Exists(GamePath.Combine(staging, "BepInEx/plugins/Fika"));
        }

        if (!hasServer)
        {
            var staging = ProfilePaths.Staging(managerData, profileId);
            hasServer = Directory.Exists(GamePath.Combine(staging, SptLayout.UserMods + "/fika-server"));
        }

        if (!hasPlugin)
        {
            warnings.Add("Fika client plugin not found under BepInEx/plugins/Fika. Joiners still need that plugin.");
        }

        if (mode == LaunchModes.FikaHost && !hasServer)
        {
            warnings.Add("fika-server not found under SPT_Runtime/user/mods. Host needs the Fika server package deployed.");
        }

        var incompatible = ModStore.List(managerData)
            .Where(document => document.Deployable)
            .SelectMany(document => document.Warnings)
            .Where(warning => warning.Contains("fika_compatibility=incompatible", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        warnings.AddRange(incompatible);

        return warnings;
    }

    private static string Summarize(string mode, IReadOnlyList<StartedProcess> started, string? url)
    {
        var names = string.Join(" → ", started.Select(item => item.Name));
        var text = mode switch
        {
            LaunchModes.FikaClient => "Started SPT.Launcher only (no local SPT.Server).",
            LaunchModes.FikaHost => "Started SPT.Server, then SPT.Launcher (Fika host).",
            _ => "Started SPT.Server, then SPT.Launcher (solo)."
        };
        if (url is not null)
        {
            text += " Join URL " + url + ".";
        }

        return text + " Order: " + names + ".";
    }

    private static LaunchResult Fail(string message)
        => new() { Success = false, Message = message };
}
