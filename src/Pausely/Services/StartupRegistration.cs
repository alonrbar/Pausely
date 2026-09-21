using Microsoft.Win32;

namespace Pausely.Services;

public static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        if (!enabled) { key.DeleteValue("Pausely", throwOnMissingValue: false); return; }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the Pausely executable.");
        if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "Pausely", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run Pausely.exe directly before enabling Windows startup.");
        key.SetValue("Pausely", $"\"{executable}\" --background");
    }
}
