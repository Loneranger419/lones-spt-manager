using Lones.SptManager.Core.Paths;
using Lones.SptManager.Native;

namespace Lones.SptManager.Tests;

internal sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => onReport(value);
}

internal sealed class StubFileVersionReader : Lones.SptManager.Core.Instance.IFileVersionReader
{
    private readonly Dictionary<string, string> _versions = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string fullPath, string version) => _versions[fullPath] = version;

    public string? GetFileVersion(string fullPath)
        => _versions.TryGetValue(fullPath, out var version) ? version : null;
}

internal sealed class StubVolumeIdReader : IVolumeIdReader
{
    public string? VolumeId { get; set; } = @"\\?\Volume{test}";

    public string? GetVolumeId(string path) => VolumeId;
}

internal static class GameRootFixture
{
    public static string Create41Layout(string root, bool includeUserMods = false, bool includeLegacySpt = false)
    {
        Directory.CreateDirectory(root);
        Touch(root, SptLayout.EscapeFromTarkovExe);
        Touch(root, SptLayout.WinHttpDll);
        WriteText(root, SptLayout.DoorstopConfig, "target_assembly=BepInEx\\core\\BepInEx.Preloader.dll");
        WriteText(root, SptLayout.DoorstopVersion, "4.5.0");
        WriteText(root, SptLayout.BepInExCore + "/BepInEx.Preloader.dll", "preloader");
        WriteText(root, SptLayout.BepInExPluginsSpt + "/spt-core.dll", "spt-core");
        WriteText(root, SptLayout.BepInExPluginsSpt + "/ConfigurationManager/ConfigurationManager.dll", "cm");
        WriteText(root, SptLayout.SptPrepatchDll, "prepatch");
        Touch(root, SptLayout.SptServerExe);
        Touch(root, SptLayout.SptLauncherExe);
        WriteText(root, SptLayout.SptRuntime + "/SPTarkov.Server.Core.dll", "core");
        WriteText(root, SptLayout.SptDataConfigs + "/core.json", "{\"compatibleTarkovVersion\":\"0.16.9.40743\"}");
        Directory.CreateDirectory(Combine(root, SptLayout.SptDataLauncher));
        WriteText(root, SptLayout.UserCredentials + "/credentials.json", "secret-should-not-be-read");
        if (includeUserMods)
        {
            WriteText(root, SptLayout.UserMods + "/SomeMod/mod.dll", "user-mod");
        }

        Directory.CreateDirectory(Combine(root, SptLayout.UserProfiles));
        if (includeLegacySpt)
        {
            Directory.CreateDirectory(Path.Combine(root, SptLayout.LegacySptFolder));
        }

        return root;
    }

    public static string Combine(string root, string relative)
        => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static void WriteText(string root, string relative, string contents)
    {
        var path = Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static void Touch(string root, string relative)
    {
        var path = Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x4D, 0x5A]);
    }
}
