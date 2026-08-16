using Microsoft.Win32;

namespace Lones.SptManager.App;

public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

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

    public static void ApplyWindowsTheme()
        => Apply(WindowsUsesLightTheme());

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
