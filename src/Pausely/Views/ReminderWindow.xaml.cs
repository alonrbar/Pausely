using System.Windows.Threading;

namespace Pausely.Views;

public partial class ReminderWindow : Window
{
    private readonly DispatcherTimer _dismiss;
    public ReminderWindow(string message, double seconds)
    {
        InitializeComponent();
        Message.Text = message;
        _dismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _dismiss.Tick += Dismiss;
        Loaded += (_, _) => { Position(); _dismiss.Start(); };
        Closed += (_, _) => { _dismiss.Stop(); _dismiss.Tick -= Dismiss; };
    }
    private void Position()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 20;
        Top = area.Bottom - ActualHeight - 20;
    }
    private void Dismiss(object? sender, EventArgs e) => Close();
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
}
