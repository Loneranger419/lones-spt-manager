using System.Text.Json;
using System.Text.Json.Nodes;
using Lones.SptManager.Core.Paths;

namespace Lones.SptManager.Core.Launch;

public static class LauncherUrlPatcher
{
    public const int DefaultBackendPort = 6969;

    public static string NormalizeJoinUrl(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        var value = raw.Trim().TrimEnd('/');
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Join URL must look like https://host:6969 (no trailing slash).");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Join URL must use https://.");
        }

        var port = uri.IsDefaultPort ? DefaultBackendPort : uri.Port;
        return $"https://{uri.Host}:{port}";
    }

    public static string Apply(string gameRoot, string joinUrl)
    {
        var normalized = NormalizeJoinUrl(joinUrl);
        var path = GamePath.Combine(gameRoot, SptLayout.UserLauncherConfig);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject root;
        if (File.Exists(path))
        {
            var parsed = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                         ?? throw new InvalidOperationException("Launcher config exists but is not a JSON object. Set the URL in SPT.Launcher Settings.");
            root = parsed;
        }
        else
        {
            root = [];
        }

        if (root["Url"] is not null)
        {
            root["Url"] = normalized;
        }
        else if (root["url"] is not null)
        {
            root["url"] = normalized;
        }
        else if (root["BackendUrl"] is not null)
        {
            root["BackendUrl"] = normalized;
        }
        else
        {
            root["Url"] = normalized;
        }

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return normalized;
    }
}
