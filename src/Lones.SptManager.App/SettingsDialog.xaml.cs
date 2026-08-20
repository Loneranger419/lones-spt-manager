using System.Windows.Controls;
using Lones.SptManager.Core.Instance;

namespace Lones.SptManager.App;

public partial class SettingsDialog : System.Windows.Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppTheme _originalTheme;
    private readonly string _originalManagerData;
    private readonly bool _originalUndeployOnExit;
    private bool _ready;
    private bool _saved;

    public SettingsDialog(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _originalTheme = ThemeManager.Preference;
        _originalManagerData = viewModel.ManagerData;
        _originalUndeployOnExit = viewModel.UndeployOnExit;
        ThemeBox.ItemsSource = ThemeManager.Choices;
        ThemeBox.SelectedItem = ThemeManager.ToLabel(_originalTheme);
        UndeployOnExitBox.IsChecked = _originalUndeployOnExit;
        _ready = true;
        Closing += (_, _) =>
        {
            if (!_saved)
            {
                Revert();
            }
        };
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        ThemeManager.Preview(ThemeManager.FromLabel(ThemeBox.SelectedItem as string));
    }

    private void Ok_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ThemeManager.Save(ThemeManager.FromLabel(ThemeBox.SelectedItem as string));
        var undeployOnExit = UndeployOnExitBox.IsChecked != false;
        AppSettings.SaveUndeployOnExit(InstanceStore.DefaultManagerDataPath, undeployOnExit);
        _viewModel.UndeployOnExit = undeployOnExit;
        _saved = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Revert();
        DialogResult = false;
    }

    private void Revert()
    {
        ThemeManager.Preview(_originalTheme);
        _viewModel.ManagerData = _originalManagerData;
        _viewModel.UndeployOnExit = _originalUndeployOnExit;
    }
}
