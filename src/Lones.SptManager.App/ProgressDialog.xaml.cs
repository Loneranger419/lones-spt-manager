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
    {
        StatusText.Text = progress.Message;
        Bar.IsIndeterminate = progress.Indeterminate;
        Bar.Value = progress.Percent;
        CountText.Text = progress.Total <= 0
            ? ""
            : $"{progress.Current} / {progress.Total}";
        if (!string.IsNullOrWhiteSpace(progress.LogLine))
        {
            LogBox.Items.Add(progress.LogLine);
            LogBox.ScrollIntoView(progress.LogLine);
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
