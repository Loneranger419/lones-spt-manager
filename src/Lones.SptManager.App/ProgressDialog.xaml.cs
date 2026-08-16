using Lones.SptManager.Forge;

namespace Lones.SptManager.App;

public partial class ProgressDialog : System.Windows.Window
{
    private bool _allowClose;

    public event Action? CancelRequested;

    public ProgressDialog(string title)
    {
        InitializeComponent();
        Title = title;
    }

    public void Update(ModPackProgress progress)
        => Update(progress.Message, progress.Current, progress.Total, progress.LogLine);

    public void Update(string message, int current = 0, int total = 0, string? logLine = null)
    {
        StatusText.Text = message;
        Bar.IsIndeterminate = total <= 0;
        Bar.Value = total <= 0 ? 0 : Math.Clamp(100.0 * current / total, 0, 100);
        CountText.Text = total <= 0 ? "" : $"{current} / {total}";
        if (!string.IsNullOrWhiteSpace(logLine))
        {
            LogBox.Items.Add(logLine);
            LogBox.ScrollIntoView(logLine);
        }
    }

    public void MarkFinished()
    {
        _allowClose = true;
        CancelButton.IsEnabled = false;
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
        => RequestCancel();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        RequestCancel();
    }

    private void RequestCancel()
    {
        if (_allowClose)
        {
            return;
        }

        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling…";
        CancelRequested?.Invoke();
        _allowClose = true;
        Dispatcher.BeginInvoke(Close);
    }
}
