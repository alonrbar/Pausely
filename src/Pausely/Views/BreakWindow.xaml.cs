using System.ComponentModel;

namespace Pausely.Views;

public partial class BreakWindow : Window
{
    private readonly AppController _controller;
    private bool _allowClose;
    public BreakWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Closing += OnClosing;
        Refresh();
    }
    public void Refresh()
    {
        Message.Text = _controller.Timer.Settings.BreakMessage;
        Countdown.Text = AppController.FormatTime(_controller.Timer.Remaining);
        ErrorText.Text = _controller.Timer.Error;
    }
    public void Dismiss() { _allowClose = true; Close(); }
    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = !_allowClose && !_controller.IsExiting;
    private void Lock_Click(object sender, RoutedEventArgs e) => _controller.LockAgain();
    private void Override_Click(object sender, RoutedEventArgs e) => _controller.Timer.Start();
}
