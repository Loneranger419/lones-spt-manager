using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lones.SptManager.Core.Instance;

public enum AppTheme
{
    Windows = 0,
    Dark = 1,
    Light = 2
}

public sealed class AppSettingsDocument
{
    public string Theme { get; init; } = "windows";

    /// <summary>
    /// Null when the key is missing from an older settings file. Treat as on.
    /// </summary>
    public bool? UndeployOnExit { get; init; }
}

public static class AppSettings
{
    public const string FileName = "settings.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string FilePath(string managerData)
        => Path.Combine(managerData, FileName);

    public static AppSettingsDocument Load(string managerData)
    {
        if (string.IsNullOrWhiteSpace(managerData))
        {
            return new AppSettingsDocument();
        }

        var path = FilePath(managerData);
        if (!File.Exists(path))
        {
            return new AppSettingsDocument();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettingsDocument>(File.ReadAllText(path), JsonOptions)
                   ?? new AppSettingsDocument();
        }
        catch (Exception)
        {
            return new AppSettingsDocument();
        }
    }

    public static void Save(string managerData, AppSettingsDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        Directory.CreateDirectory(managerData);
        File.WriteAllText(FilePath(managerData), JsonSerializer.Serialize(document, JsonOptions));
    }

    public static AppTheme LoadTheme(string managerData)
        => ParseTheme(Load(managerData).Theme);

    public static void SaveTheme(string managerData, AppTheme theme)
    {
        var current = Load(managerData);
        Save(managerData, new AppSettingsDocument
        {
            Theme = FormatTheme(theme),
            UndeployOnExit = current.UndeployOnExit
        });
    }

    public static bool LoadUndeployOnExit(string managerData)
        => Load(managerData).UndeployOnExit != false;

    public static void SaveUndeployOnExit(string managerData, bool undeployOnExit)
    {
        var current = Load(managerData);
        Save(managerData, new AppSettingsDocument
        {
            Theme = current.Theme,
            UndeployOnExit = undeployOnExit
        });
    }

    public static AppTheme ParseTheme(string? value)
        => (value ?? "").Trim().ToLowerInvariant() switch
        {
            "dark" => AppTheme.Dark,
            "light" => AppTheme.Light,
            _ => AppTheme.Windows
        };

    public static string FormatTheme(AppTheme theme)
        => theme switch
        {
            AppTheme.Dark => "dark",
            AppTheme.Light => "light",
            _ => "windows"
        };
}
