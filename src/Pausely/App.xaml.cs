using Pausely.Services;
using System.Threading;

namespace Pausely;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppController? _controller;
    private static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pausely");
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--smoke-test"))
        {
            var index = Array.IndexOf(e.Args, "--smoke-test");
            var output = index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke");
            Dispatcher.BeginInvoke(() => SmokeTest.Run(this, output));
            return;
        }
        _singleInstance = new Mutex(true, @"Local\Pausely.TrayApp", out var first);
        if (!first)
        {
            MessageBox.Show("Pausely is already running. Double-click its pause icon in the system tray to open it.", "Pausely", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            MessageBox.Show($"Pausely encountered a problem and will close.\n\n{args.Exception.Message}\n\nDetails: {Path.Combine(DataDirectory, "pausely.log")}", "Pausely", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        try
        {
            _controller = new AppController(new SettingsStore(DataDirectory));
            _controller.Run(e.Args.Contains("--background"));
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show(ex.Message, "Pausely couldn't start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var path = Path.Combine(DataDirectory, "pausely.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {ex}\n");
        }
        catch (Exception) { /* Logging must not prevent shutdown. */ }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
