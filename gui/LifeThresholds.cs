using System.Globalization;
using System.Text.Json;

namespace SdmmcInspector;

internal readonly record struct LifeThresholds(int NormalLimit, int CautionLimit)
{
    internal static LifeThresholds Defaults => new(60, 80);

    internal bool IsValid => NormalLimit >= 1 && NormalLimit < CautionLimit && CautionLimit <= 99;

    internal Health Classify(int upper) =>
        upper <= NormalLimit ? Health.Normal : upper <= CautionLimit ? Health.Caution : Health.Warning;
}
