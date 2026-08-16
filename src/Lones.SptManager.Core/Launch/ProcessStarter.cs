using System.Diagnostics;
using System.Net.Sockets;
using Lones.SptManager.Core.Paths;

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

        var info = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        var process = Process.Start(info)
                      ?? throw new InvalidOperationException("Failed to start " + exePath);
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
    public bool WaitUntilReady(string gameRoot, TimeSpan timeout)
    {
        var port = ReadBackendPort(gameRoot);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (CanConnect("127.0.0.1", port) || LogSaysStarted(gameRoot))
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

    private static bool LogSaysStarted(string gameRoot)
    {
        var logs = GamePath.Combine(gameRoot, SptLayout.UserLogs);
        if (!Directory.Exists(logs))
        {
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(logs, "*.log", SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(5))
        {
            try
            {
                var text = File.ReadAllText(file);
                if (text.Contains("Server has started", StringComparison.OrdinalIgnoreCase))
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
