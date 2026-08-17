using System.Diagnostics;
using System.Net.Sockets;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Launch;

public sealed class ProcessStarter : IProcessStarter
{
    public StartedProcess Start(string exePath, string workingDirectory)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        if (name.Equals("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase)
            || name.Contains("BattlEye", StringComparison.OrdinalIgnoreCase)
            || name.Contains("BEService", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to start the live EFT client or BattlEye.");
        }

        var isServer = name.Equals("SPT.Server", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = isServer,
            CreateNoWindow = false
        };
        var process = Process.Start(info)
                      ?? throw new InvalidOperationException("Failed to start " + exePath);
        if (isServer)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Closing stdin must not fail launch.
            }

            ConsoleLaunch.TryDisableQuickEdit(process.Id);
        }

        return new StartedProcess
        {
            Name = name,
            ExePath = exePath,
            WorkingDirectory = workingDirectory,
            Id = process.Id
        };
    }
}

public sealed class TcpOrLogReadyProbe : IServerReadyProbe
{
    public ServerReadySnapshot Snapshot(string gameRoot)
        => new() { LogLengths = LogLengths(gameRoot) };

    public bool WaitUntilReady(string gameRoot, TimeSpan timeout, ServerReadySnapshot snapshot)
    {
        var port = ReadBackendPort(gameRoot);
        var deadline = DateTime.UtcNow + timeout;
        var hadLogs = snapshot.LogLengths.Count > 0 || Directory.Exists(GamePath.Combine(gameRoot, SptLayout.UserLogs));
        while (DateTime.UtcNow < deadline)
        {
            if (LogSaysStartedSince(gameRoot, snapshot))
            {
                return true;
            }

            // First-run / no log folder: TCP is the only signal.
            if (!hadLogs && CanConnect("127.0.0.1", port))
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }

    public static int ReadBackendPort(string gameRoot)
    {
        var path = GamePath.Combine(gameRoot, SptLayout.SptDataConfigs + "/http.json");
        if (!File.Exists(path))
        {
            return LauncherUrlPatcher.DefaultBackendPort;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("backendPort", out var backend) && backend.TryGetInt32(out var backendPort))
            {
                return backendPort;
            }

            if (root.TryGetProperty("port", out var port) && port.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall back to the stock port; do not log the file.
        }

        return LauncherUrlPatcher.DefaultBackendPort;
    }

    private static bool CanConnect(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            return task.Wait(TimeSpan.FromMilliseconds(250)) && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Dictionary<string, long> LogLengths(string gameRoot)
    {
        var lengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var logs = GamePath.Combine(gameRoot, SptLayout.UserLogs);
        if (!Directory.Exists(logs))
        {
            return lengths;
        }

        foreach (var file in Directory.EnumerateFiles(logs, "*.log", SearchOption.AllDirectories))
        {
            try
            {
                lengths[file] = new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // Log may still be locked by the server.
            }
        }

        return lengths;
    }

    private static bool LogSaysStartedSince(string gameRoot, ServerReadySnapshot snapshot)
    {
        var logs = GamePath.Combine(gameRoot, SptLayout.UserLogs);
        if (!Directory.Exists(logs))
        {
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(logs, "*.log", SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(8))
        {
            try
            {
                var previous = snapshot.LogLengths.TryGetValue(file, out var length) ? length : 0L;
                var info = new FileInfo(file);
                if (info.Length <= previous)
                {
                    continue;
                }

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(previous, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                var added = reader.ReadToEnd();
                if (added.Contains("Server has started", StringComparison.OrdinalIgnoreCase)
                    || added.Contains("Started webserver", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // Log may still be locked by the server.
            }
        }

        return false;
    }
}
