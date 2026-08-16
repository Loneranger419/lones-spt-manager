namespace Lones.SptManager.Core.Mapping;

public sealed class ZipSlipException : InvalidOperationException
{
    public ZipSlipException(string message)
        : base(message)
    {
    }
}

public static class ArchivePathRules
{
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string NormalizeEntry(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        var normalized = raw.Replace('\\', '/').Trim();
        if (normalized.StartsWith('/'))
        {
            throw new ZipSlipException($"Rejected absolute path: {raw}");
        }

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.Trim('/');
    }

    public static bool IsDirectoryEntry(string raw)
    {
        var slashNorm = raw.Replace('\\', '/');
        return slashNorm.EndsWith('/');
    }

    public static void EnsureSafe(string rawEntry, string extractRoot)
    {
        var normalized = NormalizeEntry(rawEntry);
        if (normalized.Length == 0)
        {
            throw new ZipSlipException("Archive entry path is empty.");
        }

        if (normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new ZipSlipException($"Rejected ADS or drive path: {rawEntry}");
        }

        if (normalized.StartsWith('/') || normalized.StartsWith('\\'))
        {
            throw new ZipSlipException($"Rejected absolute path: {rawEntry}");
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part is ".." or ".")
            {
                throw new ZipSlipException($"Rejected path segment '{part}' in {rawEntry}");
            }

            var stem = part;
            var dot = stem.IndexOf('.');
            if (dot >= 0)
            {
                stem = stem[..dot];
            }

            if (DeviceNames.Contains(stem))
            {
                throw new ZipSlipException($"Rejected Windows device name in {rawEntry}");
            }
        }

        var rootFull = Path.GetFullPath(extractRoot);
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
        {
            rootFull += Path.DirectorySeparatorChar;
        }

        var dest = Path.GetFullPath(Path.Combine(extractRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new ZipSlipException($"Entry escapes extract root: {rawEntry}");
        }
    }
}
