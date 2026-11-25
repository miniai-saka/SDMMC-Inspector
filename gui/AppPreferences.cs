using System.Text.Json;

namespace SdmmcInspector;

internal readonly record struct AppPreferences(LifeThresholds Thresholds, int RefreshSeconds,
    AppLanguage Language = AppLanguage.System, bool HideSerial = false)
{
    internal const int MaximumRefresh = 24 * 60 * 60;
    internal static AppPreferences Defaults => new(LifeThresholds.Defaults, 0);

    internal static AppPreferences Load(string path, string? fallbackPath = null)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length <= 4096)
            {
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    var thresholds = new LifeThresholds(ReadNumber(root, "normalLimit"), ReadNumber(root, "cautionLimit"));
                    int seconds = ReadNumber(root, "refreshSeconds");
                    AppLanguage language = root.TryGetProperty("language", out JsonElement name) &&
                        name.ValueKind == JsonValueKind.String ? name.GetString() switch
                        {
                            "ja" => AppLanguage.Japanese,
                            "en" => AppLanguage.English,
                            _ => AppLanguage.System
                        } : AppLanguage.System;
                    bool hideSerial = root.TryGetProperty("hideSerial", out JsonElement hide) &&
                        hide.ValueKind == JsonValueKind.True;
                    return new(thresholds.IsValid ? thresholds : LifeThresholds.Defaults,
                        seconds is >= 1 and <= MaximumRefresh ? seconds : 0, language, hideSerial);
                }
            }
        }
        catch (IOException error) when (fallbackPath is not null &&
            error is FileNotFoundException or DirectoryNotFoundException)
        {
            return Load(fallbackPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing or unreadable preferences file leaves the defaults in effect.
        }
        return Defaults;
    }

    private static int ReadNumber(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : 0;

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var json = new Utf8JsonWriter(file))
            {
                json.WriteStartObject();
                json.WriteNumber("normalLimit", Thresholds.NormalLimit);
                json.WriteNumber("cautionLimit", Thresholds.CautionLimit);
                json.WriteNumber("refreshSeconds", RefreshSeconds);
                json.WriteString("language", Language switch
                {
                    AppLanguage.Japanese => "ja",
                    AppLanguage.English => "en",
                    _ => "system"
                });
                json.WriteBoolean("hideSerial", HideSerial);
                json.WriteEndObject();
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
