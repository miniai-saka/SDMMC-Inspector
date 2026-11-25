using System.Security.Cryptography;
using System.Text;

namespace SdmmcInspector;

internal enum SettingsLocation
{
    ApplicationFolder,
    LocalAppData
}

internal sealed class PreferenceStore
{
    private const string FileName = "settings.json";

    private readonly string _applicationPath;
    private readonly string _localAppDataPath;
    private readonly string _localAppDataSelection;

    internal SettingsLocation Location { get; private set; }

    internal PreferenceStore()
    {
        string applicationFolder = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string localFolder = Path.Combine(
            Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SDMMCInspector");
        _applicationPath = Path.Combine(applicationFolder, FileName);
        _localAppDataPath = Path.Combine(localFolder, FileName);

        // Store the choice per application folder, even when that folder is read-only.
        byte[] folderName = Encoding.UTF8.GetBytes(applicationFolder.ToUpperInvariant());
        string folderKey = Convert.ToHexString(SHA256.HashData(folderName));
        _localAppDataSelection = Path.Combine(localFolder, $"settings-location-{folderKey}.local");
        Location = File.Exists(_localAppDataSelection) ? SettingsLocation.LocalAppData : SettingsLocation.ApplicationFolder;
    }

    internal AppPreferences Load() => Location == SettingsLocation.LocalAppData
        ? AppPreferences.Load(_localAppDataPath)
        : AppPreferences.Load(_applicationPath, fallbackPath: _localAppDataPath);

    internal void Save(AppPreferences preferences, SettingsLocation? location = null)
    {
        SettingsLocation destination = location ?? Location;
        preferences.Save(destination == SettingsLocation.LocalAppData ? _localAppDataPath : _applicationPath);
        if (location is null)
            return;

        // Only remember the new location after saving the settings there.
        if (destination == SettingsLocation.LocalAppData)
        {
            using var selection = new FileStream(_localAppDataSelection, FileMode.OpenOrCreate, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
        }
        else
        {
            try { File.Delete(_localAppDataSelection); }
            catch (DirectoryNotFoundException) { }
        }
        Location = destination;
    }
}
