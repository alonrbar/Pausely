using System.Text.Json;

namespace Pausely.Services;

public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string FilePath => Path.Combine(directory, "settings.json");
    public string? LoadWarning { get; private set; }
    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        try
        {
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
            if (value is null || value.Validate() is not null) throw new JsonException(value?.Validate() ?? "Empty settings.");
            return value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadWarning = $"Pausely couldn't read your settings, so defaults are active. Your original file is unchanged.\n\n{ex.Message}";
            return new AppSettings();
        }
    }
    public void Save(AppSettings settings)
    {
        if (settings.Validate() is { } error) throw new ArgumentException(error);
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
