namespace SdmmcInspector;

internal enum AppLanguage { System, Japanese, English }

internal readonly record struct UiText(bool English)
{
    internal static UiText Resolve(AppLanguage language, ushort windowsLanguage) => new(language switch
    {
        AppLanguage.Japanese => false,
        AppLanguage.English => true,
        _ => (windowsLanguage & 0x03ff) != 0x11 // Primary Windows LANGID for Japanese.
    });

    internal string Pick(string japanese, string english) => English ? english : japanese;

    internal string HealthName(Health health) => health switch
    {
        Health.Healthy => Pick("健康", "Healthy"),
        Health.Normal => Pick("正常", "Normal"),
        Health.Caution => Pick("注意", "Caution"),
        Health.Warning => Pick("警告", "Warning"),
        Health.Critical => Pick("異常", "Critical"),
        Health.Exceeded => Pick("超過", "Exceeded"),
        _ => Pick("不明", "Unknown")
    };

    internal string LifeName(int index) => index switch
    {
        0 => Pick("予備ブロック", "Spare blocks"),
        1 => Pick("推定寿命 A", "Lifetime estimate A"),
        _ => Pick("推定寿命 B", "Lifetime estimate B")
    };
}
