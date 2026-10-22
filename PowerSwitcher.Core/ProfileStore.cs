using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerSwitcher.Core;

public sealed class ProfileStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProfileStore(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
    }

    public string FilePath => _filePath;

    public IReadOnlyList<PowerProfile> Load()
    {
        if (!File.Exists(_filePath))
            return Array.Empty<PowerProfile>();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<PowerProfile>>(json, JsonOptions)
                ?? new List<PowerProfile>();
        }
        catch
        {
            return Array.Empty<PowerProfile>();
        }
    }

    public bool Save(IEnumerable<PowerProfile> profiles)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(profiles, JsonOptions);
            File.WriteAllText(_filePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string? LoadCurrentProfileName()
    {
        try
        {
            var path = GetCurrentProfilePath();
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<string>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public bool SaveCurrentProfileName(string? name)
    {
        try
        {
            var path = GetCurrentProfilePath();

            if (name is null)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return true;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(name));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public AutoSwitchSettings LoadAutoSwitchSettings()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
                return new AutoSwitchSettings();

            return JsonSerializer.Deserialize<AutoSwitchSettings>(File.ReadAllText(path), JsonOptions)
                ?? new AutoSwitchSettings();
        }
        catch
        {
            return new AutoSwitchSettings();
        }
    }

    public bool SaveAutoSwitchSettings(AutoSwitchSettings settings)
    {
        try
        {
            var path = GetSettingsPath();

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetSettingsPath() => GetSiblingPath("settings.json");

    private string GetCurrentProfilePath() => GetSiblingPath("current-profile.json");

    private string GetSiblingPath(string fileName)
    {
        var directory = Path.GetDirectoryName(_filePath);
        return Path.Combine(
            string.IsNullOrEmpty(directory) ? "." : directory,
            fileName);
    }

    private static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "PowerSwitcher", "profiles.json");
    }
}
