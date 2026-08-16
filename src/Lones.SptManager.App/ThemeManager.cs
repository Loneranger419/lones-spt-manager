using Lones.SptManager.Core.Instance;
using Microsoft.Win32;

namespace Lones.SptManager.App;

public static class ThemeManager
{
    public const string FollowWindowsLabel = "Follow Windows";
    public const string DarkLabel = "Dark";
    public const string LightLabel = "Light";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static IReadOnlyList<string> Choices { get; } = [FollowWindowsLabel, DarkLabel, LightLabel];

    public static AppTheme Preference { get; private set; } = AppTheme.Windows;

    public static bool FollowsWindows => Preference == AppTheme.Windows;

    public static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var value = key?.GetValue("AppsUseLightTheme");
            return value is not int integer || integer != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static void ApplySaved()
    {
        Preference = AppSettings.LoadTheme(InstanceStore.DefaultManagerDataPath);
        ApplyPreference();
    }

    public static void ApplyPreference()
    {
        var light = Preference switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => WindowsUsesLightTheme()
        };
        Apply(light);
    }

    public static void Preview(AppTheme theme)
    {
        Preference = theme;
        ApplyPreference();
    }

    public static void Save(AppTheme theme)
    {
        Preference = theme;
        AppSettings.SaveTheme(InstanceStore.DefaultManagerDataPath, theme);
        ApplyPreference();
    }

    public static AppTheme FromLabel(string? label)
        => label switch
        {
            DarkLabel => AppTheme.Dark,
            LightLabel => AppTheme.Light,
            _ => AppTheme.Windows
        };

    public static string ToLabel(AppTheme theme)
        => theme switch
        {
            AppTheme.Dark => DarkLabel,
            AppTheme.Light => LightLabel,
            _ => FollowWindowsLabel
        };

    public static void Apply(bool light)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        var theme = new System.Windows.ResourceDictionary
        {
            Source = new Uri(
                light
                    ? "pack://application:,,,/Themes/Theme.Light.xaml"
                    : "pack://application:,,,/Themes/Theme.Dark.xaml")
        };

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(theme);
            return;
        }

        merged[0] = theme;
    }
}
