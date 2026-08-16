namespace Lones.SptManager.Core.Paths;

public static class SptLayout
{
    public const string EscapeFromTarkovExe = "EscapeFromTarkov.exe";
    public const string BepInEx = "BepInEx";
    public const string WinHttpDll = "winhttp.dll";
    public const string DoorstopConfig = "doorstop_config.ini";
    public const string DoorstopVersion = ".doorstop_version";
    public const string SptRuntime = "SPT_Runtime";
    public const string LegacySptFolder = "SPT";
    public const string SptServerExe = "SPT_Runtime/SPT.Server.exe";
    public const string SptLauncherExe = "SPT_Runtime/SPT.Launcher.exe";
    public const string BepInExConfig = "BepInEx/config";
    public const string BepInExCore = "BepInEx/core";
    public const string BepInExPluginsSpt = "BepInEx/plugins/spt";
    public const string SptPrepatchDll = "BepInEx/patchers/spt-prepatch.dll";
    public const string SptData = "SPT_Runtime/SPT_Data";
    public const string SptDataConfigs = "SPT_Runtime/SPT_Data/configs";
    public const string SptDataLauncher = "SPT_Runtime/SPT_Data/Launcher";
    public const string UserMods = "SPT_Runtime/user/mods";
    public const string UserProfiles = "SPT_Runtime/user/profiles";
    public const string UserPatchers = "SPT_Runtime/user/patchers";
    public const string UserLauncherConfig = "SPT_Runtime/user/launcher/config.json";
    public const string UserLogs = "SPT_Runtime/user/logs";
    public const string UserCerts = "SPT_Runtime/user/certs";
    public const string UserCredentials = "SPT_Runtime/user/credentials";

    public const string ExpectedEftFileVersion = "0.16.9.40743";
    public const string ExpectedSptVersionPrefix = "4.1";

    public static readonly string[] RequiredGameRootFiles =
    [
        EscapeFromTarkovExe,
        WinHttpDll,
        DoorstopConfig
    ];

    public static readonly string[] RequiredGameRootDirectories =
    [
        BepInEx,
        SptRuntime
    ];

    public static readonly string[] RequiredRuntimeFiles =
    [
        SptServerExe,
        SptLauncherExe
    ];
}
