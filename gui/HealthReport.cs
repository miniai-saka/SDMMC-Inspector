using System.Buffers.Binary;
using System.Text;

namespace SdmmcInspector;

internal enum Health { Healthy, Normal, Unknown, Caution, Warning, Critical, Exceeded }

internal sealed record RegisterRow(string Name, string Address, Health? Status, string Raw, string Meaning);
internal readonly record struct HealthIndicator(Health State, string Detail);

internal sealed record HealthReport(
    string Summary, string Detail, IReadOnlyList<RegisterRow> Rows, string RawText = "",
    ulong RegisterCapacity = 0, HealthIndicator[]? Indicators = null, string? Notice = null,
    EmmcOverview? Emmc = null, byte[]? ExtCsdBytes = null)
{
    private static readonly string[] LifeRegisters =
        ["PRE_EOL_INFO", "DEVICE_LIFE_TIME_EST_TYP_A", "DEVICE_LIFE_TIME_EST_TYP_B"];
    private static readonly string[] AllocationUnits =
        ["", "16 KB", "32 KB", "64 KB", "128 KB", "256 KB", "512 KB", "1 MB",
         "2 MB", "4 MB", "8 MB", "12 MB", "16 MB", "24 MB", "32 MB", "64 MB"];

    internal static HealthReport Unavailable(string summary, string detail) =>
        new(summary, detail, []);

    internal static HealthReport From(Inspection read, Device fallback, bool probe, LifeThresholds thresholds, UiText text)
    {
        Device device = read.Device ?? fallback;
        if (read.OpenError != 0)
            return Unavailable(text.Pick("機器にアクセスできません", "Device is inaccessible"),
                WindowsIssue(read.OpenError, text));

        string unavailable = text.Pick("レジスタを取得できません", "Registers unavailable");
        return device.Protocol switch
        {
            CardProtocol.Mmc => FromMmc(read.ExtCsd, thresholds, text),
            CardProtocol.Sd => FromSd(read, probe, text),
            _ when device.BusType == 7 => Unavailable(unavailable, text.Pick(
                "この USB 接続では Windows から SD/MMC レジスタを読み取れません。",
                "Windows cannot read SD/MMC registers over this USB connection.")),
            _ => Unavailable(unavailable, device.ProtocolError == 5
                ? WindowsIssue(5, text)
                : text.Pick("Windows からネイティブ SD/MMC プロトコルを確認できませんでした。",
                    "Windows did not report a native SD/MMC protocol.")
                  + (device.ProtocolError != 0 ? $" (Win32 {device.ProtocolError})" : ""))
        };
    }

    private static HealthReport FromMmc(RegisterRead? read, LifeThresholds thresholds, UiText text)
    {
        if (read?.Bytes is not { Length: 512 } data || Array.TrueForAll(data, value => value == 0))
        {
            string issue = read?.Bytes is { Length: 512 } ? ReadIssue(new(null, "all-zero"), text) : ReadIssue(read, text);
            return Unavailable(text.Pick("寿命情報を取得できません", "Lifetime data unavailable"),
                text.Pick($"EXT_CSD を取得できませんでした。{issue}", $"Could not read EXT_CSD. {issue}"));
        }

        byte revision = data[192];
        uint sectorCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(212, 4));
        ulong capacity = revision >= 2 && ExtCsd.IsKnownRevision(revision) ? 512UL * sectorCount : 0;
        List<RegisterRow> rows = [];
        HealthIndicator[] indicators = new HealthIndicator[LifeRegisters.Length];
        bool supportsHealth = revision >= 7 && ExtCsd.IsKnownRevision(revision);
        for (int i = 0; i < LifeRegisters.Length; i++)
        {
            if (!supportsHealth)
            {
                indicators[i] = new(Health.Unknown, text.Pick("定義なし", "N/A"));
                continue;
            }
            int address = 267 + i;
            byte value = data[address];
            (Health health, string meaning, string indicator) =
                i == 0 ? SpareBlocks(value, text) : LifeUsed(value, thresholds, text);
            indicators[i] = new(health, indicator);
            if (health != Health.Unknown)
                rows.Add(new(LifeRegisters[i], address.ToString(), health, Hex(value), meaning));
        }

        rows.AddRange(ExtCsd.Rows(data, text));
        return new("eMMC", "", rows, HexDump(("EXT_CSD", data)), capacity, indicators,
            Emmc: ExtCsd.Overview(data, text), ExtCsdBytes: data);
    }

    private static (Health, string, string) SpareBlocks(byte value, UiText text) => value switch
    {
        1 => (Health.Healthy, text.Pick("予備ブロック消費 80% 未満", "Less than 80% of spare blocks used"), text.Pick("80% 未満", "< 80%")),
        2 => (Health.Caution, text.Pick("予備ブロック消費 80% 到達", "Spare block use reached 80%"), text.Pick("80% に到達", "≥ 80%")),
        3 => (Health.Critical, text.Pick("予備ブロック消費 90% 到達", "Spare block use reached 90%"), text.Pick("90% に到達", "≥ 90%")),
        0 => (Health.Unknown, text.Pick("機器による報告なし", "Not reported by the device"), text.Pick("報告なし", "Not set")),
        _ => (Health.Unknown, text.Pick("予約値", "Reserved"), text.Pick("予約値", "Reserved"))
    };

    private static (Health, string, string) LifeUsed(byte value, LifeThresholds thresholds, UiText text)
    {
        if (value == 0)
            return (Health.Unknown, text.Pick("機器による報告なし", "Not reported by the device"), text.Pick("報告なし", "Not set"));
        if (value == 11)
            return (Health.Exceeded, text.Pick("推定寿命を超過", "Estimated lifetime exceeded"), text.Pick("100% 超", "> 100%"));
        if (value > 11)
            return (Health.Unknown, text.Pick("予約値", "Reserved"), text.Pick("予約値", "Reserved"));

        int upper = value * 10;
        string range = $"{upper - 10}–{upper}%";
        return (thresholds.Classify(upper), text.Pick($"推定寿命の消費 {range}", $"Estimated lifetime used: {range}"), range);
    }

    private static HealthReport FromSd(Inspection read, bool probe, UiText text)
    {
        List<RegisterRow> rows = [];
        var registers = new (string Name, string Address, RegisterRead? Result, int Length)[]
        {
            (text.Pick("SD 構成レジスタ", "SD configuration register"), "SCR / ACMD51", read.Scr, 8),
            (text.Pick("SD ステータス", "SD Status"), "SD Status / ACMD13", read.SdStatus, 64),
            (text.Pick("切り替え機能情報", "Switch function information"),
                text.Pick("CMD6 / チェックモード", "CMD6 / check mode"), read.Cmd6, 64)
        };
        foreach (var entry in registers)
        {
            if (entry.Result?.Bytes is not null)
                rows.Add(new(entry.Name, entry.Address, null, $"{entry.Length} B", text.Pick("取得済み", "Read successfully")));
        }

        if (read.Scr?.Bytes is { } scr)
        {
            int structure = scr[0] >> 4;
            int spec = scr[0] & 15;
            int widths = scr[1] & 15;
            int spec3 = (scr[2] >> 7) & 1;
            int spec4 = (scr[2] >> 2) & 1;
            int specx = ((scr[2] & 3) << 2) | (scr[3] >> 6);
            rows.Add(new(text.Pick("SCR 構造版", "SCR structure version"), "SCR_STRUCTURE [63:60]", null, Hex((uint)structure),
                structure == 0 ? text.Pick("SCR 構造 0", "SCR structure 0") : text.Pick("未対応の構造版", "Unsupported structure")));
            if (structure == 0)
            {
                rows.Add(new(text.Pick("SD 仕様コード", "SD specification code"), "SD_SPEC [59:56]", null, Hex((uint)spec),
                    $"SD_SPEC3={spec3} / SD_SPEC4={spec4} / SD_SPECX={specx}"));
                rows.Add(new(text.Pick("対応バス幅", "Supported bus widths"), "SD_BUS_WIDTHS [51:48]", null, Hex((uint)widths),
                    (widths & 5) switch { 5 => "1 bit / 4 bit", 4 => "4 bit", 1 => "1 bit", _ => text.Pick("未定義", "Undefined") }));
            }
        }
        if (read.SdStatus?.Bytes is { } status)
        {
            uint speed = status[8];
            uint allocationUnit = (uint)(status[10] >> 4);
            string speedName = speed switch
            {
                0 => "Class 0", 1 => "Class 2", 2 => "Class 4", 3 => "Class 6", 4 => "Class 10", _ => text.Pick("予約値", "Reserved")
            };
            rows.Add(new(text.Pick("スピードクラス", "Speed class"), "SPEED_CLASS [447:440]", null, Hex(speed), speedName));
            rows.Add(new(text.Pick("アロケーションユニット", "Allocation unit"), "AU_SIZE [431:428]", null, Hex(allocationUnit),
                allocationUnit == 0 ? text.Pick("未定義", "Undefined") : AllocationUnits[allocationUnit]));
        }

        int received = registers.Count(register => register.Result?.Bytes is not null);
        string detail = !probe ? text.Pick("「機能」→「SD レジスタ読取（試験）」で追加レジスタを取得できます。",
                "Use Tools → Read SD Registers (Experimental) to read more registers.")
            : read.SdOpenError != 0 ? WindowsIssue(read.SdOpenError, text)
            : read.SdProtocolChanged ? text.Pick("読み取り直前に SD プロトコルを確認できませんでした。",
                "Could not confirm the SD protocol before reading the registers.")
            : received > 0 ? ""
            : ReadIssue(registers.FirstOrDefault(entry => entry.Result is not null).Result, text);
        string? notice = probe && received is > 0 and < 3
            ? text.Pick($"SD レジスタ {received} / 3 件を取得", $"Read {received} of 3 SD registers") : null;
        return new(text.Pick("寿命情報の取得対象外", "Lifetime data unavailable"), detail, rows,
            HexDump(("SCR", read.Scr?.Bytes), ("SD Status", read.SdStatus?.Bytes), ("CMD6 check", read.Cmd6?.Bytes)),
            Notice: notice);
    }

    private static string ReadIssue(RegisterRead? read, UiText text) => read?.Issue switch
    {
        "win32" => WindowsIssue(read.Code, text),
        "length" => text.Pick($"応答の長さが不正です ({read.Received} バイト)。", $"Unexpected response length ({read.Received} bytes)."),
        "all-zero" => text.Pick("応答が全バイト 0 です。", "The response contains only zero bytes."),
        "invalid-data" => text.Pick("応答データの形式が不正です。", "The response format is invalid."),
        _ => text.Pick("応答がありません。", "The device did not respond.")
    };

    private static string WindowsIssue(uint code, UiText text) => code switch
    {
        5 => text.Pick("アクセスが拒否されました。管理者として実行してください (Win32 5)。",
            "Access denied. Try running as administrator (Win32 5)."),
        32 => text.Pick("別のアプリが使用中です (Win32 32)。", "Another app is using the device (Win32 32)."),
        2 or 3 or 21 or 1167 => text.Pick($"機器が接続されていないか、準備ができていません (Win32 {code})。",
            $"The device is disconnected or not ready (Win32 {code})."),
        1 or 50 => text.Pick($"ドライバーがこの読み取りに対応していません (Win32 {code})。",
            $"The driver does not support this read (Win32 {code})."),
        _ => text.Pick($"読み取りに失敗しました (Win32 {code})。", $"Could not read the device (Win32 {code}).")
    };

    private static string Hex(uint value) => "0x" + value.ToString("X2");

    private static string HexDump(params (string Name, byte[]? Data)[] registers)
    {
        StringBuilder text = new();
        foreach (var register in registers)
        {
            if (register.Data is not { } data)
                continue;
            if (text.Length != 0)
                text.Append("\r\n");
            text.Append(register.Name).Append("  (").Append(data.Length).Append(" bytes)\r\n");
            for (int offset = 0; offset < data.Length; offset += 16)
            {
                text.Append(offset.ToString("X4")).Append("  ");
                foreach (byte value in data.AsSpan(offset, Math.Min(16, data.Length - offset)))
                    text.Append(value.ToString("X2")).Append(' ');
                text.Append("\r\n");
            }
        }
        return text.ToString();
    }
}
