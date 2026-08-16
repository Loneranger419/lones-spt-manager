using Lones.SptManager.Core.Profiles;

namespace Lones.SptManager.App;

public enum ProfileDialogAction
{
    Cancel = 0,
    Accept = 1,
    Copy = 2,
    Delete = 3,
    Update = 4
}

public sealed class ProfileDialogResult
{
    public ProfileDialogAction Action { get; init; } = ProfileDialogAction.Cancel;
    public string Name { get; init; } = "";
    public string? CopyFromId { get; init; }
    public string? PackSource { get; init; }
    public ProfileCopyOptions Options { get; init; } = ProfileCopyOptions.All;
}

public partial class ProfileDialog : System.Windows.Window
{
    public ProfileDialogAction Action { get; private set; } = ProfileDialogAction.Cancel;

    public string EnteredName => NameBox.Text.Trim();

    public ProfileDialog(
        string title,
        string prompt,
        string initialName,
        bool allowCopy,
        IReadOnlyList<string>? copyFromIds,
        string? packSource = null)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        NameBox.Text = initialName;
        CopyButton.Visibility = allowCopy ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        DeleteButton.Visibility = allowCopy ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        PackPanel.Visibility = allowCopy ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        var hasPack = !string.IsNullOrWhiteSpace(packSource);
        UpdateButton.Visibility = allowCopy && hasPack
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        SavedPackPanel.Visibility = allowCopy && hasPack
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        if (hasPack)
        {
            SavedPackText.Text = packSource;
        }
        var sources = copyFromIds ?? [];
        if (sources.Count == 0)
        {
            CopyFromPanel.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            CopyFromBox.Items.Add("(none)");
            foreach (var id in sources)
            {
                CopyFromBox.Items.Add(id);
            }

            CopyFromBox.SelectedIndex = 0;
            SetCopyOptionsEnabled(false);
        }

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public static ProfileDialogResult ShowAdd(
        System.Windows.Window owner,
        IReadOnlyList<string> existingIds)
    {
        var dialog = new ProfileDialog(
            "Add profile",
            "Name the new profile. Optionally install a Forge pack from a mods.json link, or copy from an existing profile.",
            "",
            allowCopy: false,
            existingIds)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog.ToResult();
    }

    public static ProfileDialogResult ShowEdit(
        System.Windows.Window owner,
        string currentName,
        string? packSource = null)
    {
        var dialog = new ProfileDialog(
            "Edit profile",
            "Rename this profile, copy it, or delete it. Delete removes this profile's saves / configs / Overwrite / generated files. Store mods stay.",
            currentName,
            allowCopy: true,
            copyFromIds: null,
            packSource)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog.ToResult();
    }

    private ProfileDialogResult ToResult()
        => new()
        {
            Action = Action,
            Name = EnteredName,
            CopyFromId = string.IsNullOrWhiteSpace(EnteredPackSource) ? SelectedCopyFromId() : null,
            PackSource = string.IsNullOrWhiteSpace(EnteredPackSource) ? null : EnteredPackSource,
            Options = ReadOptions()
        };

    private string EnteredPackSource => PackSourceBox.Text.Trim().Trim('"');

    private string? SelectedCopyFromId()
    {
        if (CopyFromPanel.Visibility != System.Windows.Visibility.Visible
            || CopyFromBox.SelectedIndex <= 0
            || CopyFromBox.SelectedItem is not string id
            || id == "(none)")
        {
            return null;
        }

        return id;
    }

    private ProfileCopyOptions ReadOptions()
        => new()
        {
            Saves = CopySavesBox.IsChecked == true,
            RuntimeFiles = CopyRuntimeBox.IsChecked == true,
            BepInExConfig = CopyConfigBox.IsChecked == true,
            Overwrite = CopyOverwriteBox.IsChecked == true,
            EnabledMods = CopyEnabledBox.IsChecked == true,
            LaunchSettings = true
        };

    private void PackSource_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (CopyFromBox is null || CopySavesBox is null)
        {
            return;
        }

        var hasPack = !string.IsNullOrWhiteSpace(EnteredPackSource);
        if (hasPack && CopyFromBox.Items.Count > 0)
        {
            CopyFromBox.SelectedIndex = 0;
        }

        CopyFromBox.IsEnabled = !hasPack;
        SetCopyOptionsEnabled(!hasPack && CopyFromBox.SelectedIndex > 0);
    }

    private void BrowsePack_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a mods.json pack",
            Filter = "Mod pack JSON|*.json|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            PackSourceBox.Text = dialog.FileName;
        }
    }

    private void CopyFrom_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => SetCopyOptionsEnabled(string.IsNullOrWhiteSpace(EnteredPackSource) && CopyFromBox.SelectedIndex > 0);

    private void SetCopyOptionsEnabled(bool enabled)
    {
        CopySavesBox.IsEnabled = enabled;
        CopyRuntimeBox.IsEnabled = enabled;
        CopyConfigBox.IsEnabled = enabled;
        CopyOverwriteBox.IsEnabled = enabled;
        CopyEnabledBox.IsEnabled = enabled;
    }

    private void Ok_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EnteredName))
        {
            return;
        }

        Action = ProfileDialogAction.Accept;
        DialogResult = true;
    }

    private void Update_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Action = ProfileDialogAction.Update;
        DialogResult = true;
    }

    private void Copy_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EnteredName))
        {
            return;
        }

        Action = ProfileDialogAction.Copy;
        DialogResult = true;
    }

    private void Delete_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Action = ProfileDialogAction.Delete;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Action = ProfileDialogAction.Cancel;
        DialogResult = false;
    }
}
