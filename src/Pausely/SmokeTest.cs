using Pausely.Services;
using Pausely.Views;
using System.Globalization;
using System.Windows.Controls;
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
            CheckSettingsEditing(controller);
            Render(new MainWindow(controller), output, "dashboard", 558, 728);
            Render(new SettingsWindow(controller), output, "settings", 568, 818);
            Render(new SettingsWindow(controller), output, "settings-small", 528, 558);
            Render(new ReminderWindow("Quick break in one minute", 6), output, "reminder", 420, 235);
            controller.Timer.TakeBreak();
            controller.Timer.SetAway(true);
            Render(new BreakWindow(controller), output, "break", 498, 548);
            File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS: four WPF views rendered (including compact settings); settings edit/revert/save checks, round trip and corrupt-file recovery passed; native session subscription succeeded (away={away}). No workstation lock or startup registration performed.");
            app.Shutdown(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "result.txt"), ex.ToString());
            app.Shutdown(1);
        }
    }
    private static void CheckSettingsEditing(AppController controller)
    {
        var original = controller.Timer.Settings;
        var window = new SettingsWindow(controller);
        void Expect(bool enabled)
        {
            if (window.SaveButton.IsEnabled != enabled) throw new InvalidOperationException($"Expected Save Settings enabled={enabled}.");
        }
        Expect(false);
        window.FocusInput.Text = (original.FocusMinutes + 1).ToString(CultureInfo.CurrentCulture);
        Expect(true);
        window.FocusInput.Text = original.FocusMinutes.ToString("0.0", CultureInfo.CurrentCulture);
        Expect(false); // Equivalent numeric formatting is not a changed preference.
        window.ReminderInput.Text += "!";
        Expect(true);
        window.ReminderInput.Text = original.ReminderMessage;
        Expect(false);
        window.FocusInput.Text = "";
        window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (controller.Timer.Settings != original || string.IsNullOrEmpty(window.ValidationText.Text))
            throw new InvalidOperationException("Invalid edits must stay unsaved and show validation.");
        window.FocusInput.Text = original.FocusMinutes.ToString(CultureInfo.CurrentCulture);
        Expect(false);
        window.SoundInput.IsChecked = !original.PlaySound;
        Expect(true);
        window.SoundInput.IsChecked = original.PlaySound;
        Expect(false);
        window.AutoStartInput.IsChecked = !original.AutoStartTimer;
        Expect(true);
        window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (controller.Timer.Settings != original with { AutoStartTimer = !original.AutoStartTimer })
            throw new InvalidOperationException("Save Settings did not apply the edited preference.");
        controller.SaveSettings(original);
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
        var clientWidth = (int)Math.Ceiling(window.ActualWidth);
        var clientHeight = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new RenderTargetBitmap(clientWidth, clientHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        // A white backdrop makes the transparent window shadow visible in review images.
        var preview = new DrawingVisual();
        using (var drawing = preview.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, clientWidth, clientHeight));
            drawing.DrawImage(bitmap, new Rect(0, 0, clientWidth, clientHeight));
        }
        var flattened = new RenderTargetBitmap(clientWidth, clientHeight, 96, 96, PixelFormats.Pbgra32);
        flattened.Render(preview);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(flattened));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        window.Hide();
    }
}
