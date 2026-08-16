using System.IO;

namespace Lones.SptManager.App;

internal static class FolderPicker
{
    public static string? Pick(string description, string? initial)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
        {
            dialog.SelectedPath = initial;
        }

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }
}
