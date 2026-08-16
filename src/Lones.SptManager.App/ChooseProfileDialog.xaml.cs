namespace Lones.SptManager.App;

public partial class ChooseProfileDialog : System.Windows.Window
{
    public string? SelectedId { get; private set; }

    public ChooseProfileDialog(string title, string prompt, IReadOnlyList<string> profileIds)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        foreach (var id in profileIds)
        {
            ProfileBox.Items.Add(id);
        }

        if (ProfileBox.Items.Count > 0)
        {
            ProfileBox.SelectedIndex = 0;
        }
    }

    public static string? Show(
        System.Windows.Window owner,
        string title,
        string prompt,
        IReadOnlyList<string> profileIds)
    {
        var dialog = new ChooseProfileDialog(title, prompt, profileIds) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedId : null;
    }

    private void Ok_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not string id || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        SelectedId = id;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
