using Microsoft.Win32;

namespace Lones.SptManager.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        ThemeManager.ApplyWindowsTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        Dispatcher.Invoke(ThemeManager.ApplyWindowsTheme);
    }
}
