using Pausely.Services;
using Pausely.Views;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pausely;

/// <summary>Explicit development mode: renders real WPF views without locking or changing user settings.</summary>
internal static class SmokeTest
{
    public static void Run(App app, string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var store = new SettingsStore(Path.Combine(output, "test-data"));
            var expected = new AppSettings { FocusMinutes = 30, ReminderMessage = "Time to stretch in {time}" };
            store.Save(expected);
            if (store.Load() != expected) throw new InvalidOperationException("Settings did not round trip.");
            File.WriteAllText(store.FilePath, "{broken json");
            if (store.Load() != new AppSettings() || store.LoadWarning is null) throw new InvalidOperationException("Invalid settings recovery failed.");
            using var session = new WindowsSession();
            var away = WindowsSession.IsAway();
            using var controller = new AppController(new SettingsStore(Path.Combine(output, "unused")), preview: true);
            controller.Timer.Start();
            Render(new MainWindow(controller), output, "dashboard", 520, 690);
            Render(new SettingsWindow(controller), output, "settings", 530, 780);
            Render(new ReminderWindow("Quick break in one minute", 6), output, "reminder", 400, 215);
            controller.Timer.TakeBreak();
            controller.Timer.SetAway(true);
            Render(new BreakWindow(controller), output, "break", 460, 510);
            File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS: four WPF views rendered; settings round trip and corrupt-file recovery passed; native session subscription succeeded (away={away}). No workstation lock or startup registration performed.");
            app.Shutdown(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "result.txt"), ex.ToString());
            app.Shutdown(1);
        }
    }
    private static void Render(Window window, string output, string name, int width, int height)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowActivated = false;
        window.Left = -10000;
        window.Top = -10000;
        window.Width = width;
        window.Height = height;
        window.Show();
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var clientWidth = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
        var clientHeight = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
        var bitmap = new RenderTargetBitmap(clientWidth, clientHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        window.Hide();
    }
}
