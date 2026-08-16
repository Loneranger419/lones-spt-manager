using System.Diagnostics;

namespace Lones.SptManager.Core.Instance;

public sealed class FileVersionReader : IFileVersionReader
{
    public string? GetFileVersion(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var version = FileVersionInfo.GetVersionInfo(fullPath).FileVersion;
        return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
    }
}
