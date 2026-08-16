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

    public static AppTheme LoadTheme(string managerData)
    {
        if (string.IsNullOrWhiteSpace(managerData))
        {
            return AppTheme.Windows;
        }

        var path = FilePath(managerData);
        if (!File.Exists(path))
        {
            return AppTheme.Windows;
        }

        try
        {
            var document = JsonSerializer.Deserialize<AppSettingsDocument>(File.ReadAllText(path), JsonOptions);
            return ParseTheme(document?.Theme);
        }
        catch (Exception)
        {
            return AppTheme.Windows;
        }
    }

    public static void SaveTheme(string managerData, AppTheme theme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        Directory.CreateDirectory(managerData);
        File.WriteAllText(
            FilePath(managerData),
            JsonSerializer.Serialize(new AppSettingsDocument { Theme = FormatTheme(theme) }, JsonOptions));
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
