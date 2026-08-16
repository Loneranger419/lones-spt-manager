using System.Security.Cryptography;
using Lones.SptManager.Core.Paths;

namespace Lones.SptManager.Core.Instance;

public sealed record BaselineFile(string RelativePath, string Sha256);

public sealed record SptOwnedBaseline(IReadOnlyList<BaselineFile> Files)
{
    public IReadOnlySet<string> RelativePaths { get; } = Files
        .Select(file => GamePath.Normalize(file.RelativePath))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public sealed class SptOwnedBaselineBuilder
{
    public SptOwnedBaseline Build(string gameRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        var root = Path.GetFullPath(gameRoot);
        var files = new List<BaselineFile>();

        AddIfFile(files, root, SptLayout.WinHttpDll);
        AddIfFile(files, root, SptLayout.DoorstopConfig);
        AddIfFile(files, root, SptLayout.DoorstopVersion);
        AddIfFile(files, root, SptLayout.SptPrepatchDll);
        AddDirectory(files, root, SptLayout.BepInExCore);
        AddDirectory(files, root, SptLayout.BepInExPluginsSpt);
        AddRuntimeBinaries(files, root);
        AddDirectory(files, root, SptLayout.SptDataConfigs);

        files.Sort((left, right) => string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));
        return new SptOwnedBaseline(files);
    }

    private static void AddRuntimeBinaries(List<BaselineFile> files, string root)
    {
        var runtime = Path.Combine(root, SptLayout.SptRuntime);
        if (!Directory.Exists(runtime))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(runtime, "*.*", SearchOption.TopDirectoryOnly))
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file);
            files.Add(new BaselineFile(GamePath.Normalize(relative), HashFile(file)));
        }
    }

    private static void AddDirectory(List<BaselineFile> files, string root, string relativeDirectory)
    {
        var full = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(full))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            files.Add(new BaselineFile(GamePath.Normalize(relative), HashFile(file)));
        }
    }

    private static void AddIfFile(List<BaselineFile> files, string root, string relative)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            return;
        }

        files.Add(new BaselineFile(GamePath.Normalize(relative), HashFile(full)));
    }

    private static string HashFile(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
