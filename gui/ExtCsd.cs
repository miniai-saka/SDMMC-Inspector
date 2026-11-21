namespace SdmmcInspector;

internal sealed record EmmcOverview(string Specification, string? Firmware, string? TransferMode, string? Cache, string Queue);

internal static class ExtCsd
{
    private enum Format
    {
        Raw, Bytes, Firmware, Count, Sectors512, KiB, KiB4, KiB128, KiB512, GroupSize,
        Time10, Time300, SupportBit, EnabledBit, QueueDepth, SectorSize, CardType, BusWidth, Timing,
        ErasedValue, BackgroundStatus
    }

    private enum Feature
    {
        Always, Queue, Barrier, Firmware, FirmwareInstall, Cache, Background, Hpi,
        Packed, Tag, Context, ExtendedPartitions, Partitions, Enhanced, Boot, ProductState,
        SecurityError, SecureTrim, SecureErase, Trim, Ddr52, Hs200, Hs400, Native4K
    }

    private sealed record Field(int Offset, string Id, string Japanese, string English, int Since = 0, int Length = 1,
        Format Encoding = Format.Raw, Feature Needs = Feature.Always, bool RequireValue = false);

    // JESD84-B51 section 7.4 (Table 93), with earlier revisions checked against
    // JESD84-A441 and the revision checks in the Linux MMC driver.
    // The four write-only control bytes, reserved bytes and the lifetime fields
    // are excluded here. Lifetime fields have their own availability and policy.
    private static readonly Field[] Fields =
    [
        new(212, "SEC_COUNT", "ユーザー領域", "User area", 2, 4, Format.Sectors512, RequireValue: true),
        new(196, "DEVICE_TYPE", "対応転送モード", "Supported transfer modes", Encoding: Format.CardType),
        new(185, "HS_TIMING", "タイミングモード", "Timing mode", Encoding: Format.Timing),
        new(183, "BUS_WIDTH", "データバス幅", "Data bus width", Encoding: Format.BusWidth),
        new(254, "FIRMWARE_VERSION", "ファームウェア版", "Firmware version", 7, 8, Format.Firmware, RequireValue: true),
        new(262, "DEVICE_VERSION", "デバイス版", "Device version", 7, 2),
        new(249, "CACHE_SIZE", "キャッシュ容量", "Cache capacity", 6, 4, Format.KiB),
        new(308, "CMDQ_SUPPORT", "コマンドキュー", "Command queue", 8, Encoding: Format.SupportBit),
        new(307, "CMDQ_DEPTH", "キュー深度", "Queue depth", 8, Encoding: Format.QueueDepth, Needs: Feature.Queue),
        new(15, "CMDQ_MODE_EN", "キューモード", "Queue mode", 8, Encoding: Format.EnabledBit, Needs: Feature.Queue),
        new(194, "CSD_STRUCTURE", "CSD 構造版", "CSD structure version", Encoding: Format.Count),
        new(197, "DRIVER_STRENGTH", "対応ドライブ強度のビットマップ", "Supported driver strengths bitmap", 6),
        new(184, "STROBE_SUPPORT", "拡張ストローブ", "Enhanced strobe", 8, Encoding: Format.SupportBit, Needs: Feature.Hs400),
        new(226, "BOOT_SIZE_MULT", "ブート領域あたりの容量", "Capacity per boot partition", 3, Encoding: Format.KiB128),
        new(168, "RPMB_SIZE_MULT", "RPMB 領域の容量", "RPMB partition capacity", 5, Encoding: Format.KiB128),
        new(61, "DATA_SECTOR_SIZE", "現在のデータセクター", "Active data sector", 6, Encoding: Format.SectorSize),
        new(63, "NATIVE_SECTOR_SIZE", "ネイティブセクター", "Native data sector", 6, Encoding: Format.SectorSize),
        new(62, "USE_NATIVE_SECTOR", "ネイティブセクターの使用設定", "Native sector setting", 6, Encoding: Format.EnabledBit, Needs: Feature.Native4K),
        new(266, "OPTIMAL_READ_SIZE", "推奨読み取り単位", "Optimal read size", 7, Encoding: Format.KiB4, RequireValue: true),
        new(265, "OPTIMAL_WRITE_SIZE", "推奨書き込み単位", "Optimal write size", 7, Encoding: Format.KiB4, RequireValue: true),
        new(264, "OPTIMAL_TRIM_UNIT_SIZE", "推奨 TRIM 単位コード", "Optimal TRIM unit code", 7, Needs: Feature.Trim, RequireValue: true),
        new(505, "EXT_SECURITY_ERR", "拡張セキュリティコマンドのエラー情報", "Extended security command error information", 7, Needs: Feature.SecurityError),
        new(504, "S_CMD_SET", "対応コマンドセットのビットマップ", "Supported command sets bitmap"),
        new(503, "HPI_FEATURES", "高優先度割り込み HPI の対応情報", "High-priority interrupt (HPI) support", 5),
        new(502, "BKOPS_SUPPORT", "バックグラウンド処理", "Background operations", 5, Encoding: Format.SupportBit),
        new(501, "MAX_PACKED_READS", "一括読み取りの上限", "Maximum packed reads", 6, Encoding: Format.Count),
        new(500, "MAX_PACKED_WRITES", "一括書き込みの上限", "Maximum packed writes", 6, Encoding: Format.Count),
        new(499, "DATA_TAG_SUPPORT", "データタグ", "Data tags", 6, Encoding: Format.SupportBit),
        new(498, "TAG_UNIT_SIZE", "データタグの単位サイズコード", "Data tag unit size code", 6, Needs: Feature.Tag),
        new(497, "TAG_RES_SIZE", "データタグ用リソース", "Data tag resources", 6, Encoding: Format.Count, Needs: Feature.Tag),
        new(496, "CONTEXT_CAPABILITIES", "コンテキスト管理の対応情報", "Context management capabilities", 6),
        new(495, "LARGE_UNIT_SIZE_M1", "大容量単位（MiB − 1）のコード", "Large unit size code (MiB minus 1)", 6),
        new(494, "EXT_SUPPORT", "拡張パーティション属性の対応情報", "Extended partition attribute support", 6),
        new(493, "SUPPORTED_MODES", "ファームウェア更新などの対応モード", "Supported modes, including firmware update", 7),
        new(492, "FFU_FEATURES", "ファームウェア更新の機能フラグ", "Firmware update feature flags", 7, Needs: Feature.Firmware),
        new(491, "OPERATION_CODES_TIMEOUT", "ファームウェア操作のタイムアウトコード", "Firmware operation timeout code", 7, Needs: Feature.FirmwareInstall, RequireValue: true),
        new(487, "FFU_ARG", "ファームウェア更新の引数", "Firmware update argument", 7, 4, Needs: Feature.Firmware),
        new(486, "BARRIER_SUPPORT", "キャッシュバリア", "Cache barrier", 8, Encoding: Format.SupportBit),
        new(302, "NUMBER_OF_FW_SECTORS_CORRECTLY_PROGRAMMED", "更新に成功したファームウェアセクター", "Firmware sectors updated successfully", 7, 4, Format.Count, Feature.Firmware),
        new(270, "VENDOR_PROPRIETARY_HEALTH_REPORT", "メーカー独自のヘルス報告", "Vendor-specific health report", 7, 32, Format.Bytes, RequireValue: true),
        new(253, "PWR_CL_DDR_200_360", "DDR 200 MHz・VCC 3.6 V の電力クラス", "DDR 200 MHz power class at 3.6 V VCC", 7, Needs: Feature.Hs400),
        new(248, "GENERIC_CMD6_TIME", "標準 CMD6 タイムアウト", "Generic CMD6 timeout", 6, Encoding: Format.Time10, RequireValue: true),
        new(247, "POWER_OFF_LONG_TIME", "長時間の電源オフ通知タイムアウト", "Long power-off notification timeout", 6, Encoding: Format.Time10, RequireValue: true),
        new(246, "BKOPS_STATUS", "バックグラウンド処理の状態", "Background operations status", 5, Encoding: Format.BackgroundStatus, Needs: Feature.Background),
        new(242, "CORRECTLY_PRG_SECTORS_NUM", "直前の書き込みで成功したセクター", "Sectors programmed successfully in the last write", 5, 4, Format.Count),
        new(241, "INI_TIMEOUT_AP", "パーティション設定後の初期化タイムアウトコード", "Initialization timeout code after partition configuration", 5, Needs: Feature.Partitions, RequireValue: true),
        new(240, "CACHE_FLUSH_POLICY", "キャッシュのフラッシュ順序", "Cache flush policy", 8, Needs: Feature.Cache, RequireValue: true),
        new(239, "PWR_CL_DDR_52_360", "DDR 52 MHz・VCC 3.6 V の電力クラス", "DDR 52 MHz power class at 3.6 V VCC", 5, Needs: Feature.Ddr52),
        new(238, "PWR_CL_DDR_52_195", "DDR 52 MHz・低電圧時の電力クラス", "DDR 52 MHz power class at low voltage", 5, Needs: Feature.Ddr52),
        new(237, "PWR_CL_200_195", "200 MHz・1.95 V の電力クラス", "200 MHz power class at 1.95 V", 6, Needs: Feature.Hs200),
        new(236, "PWR_CL_200_130", "200 MHz・1.30 V の電力クラス", "200 MHz power class at 1.30 V", 6, Needs: Feature.Hs200),
        new(235, "MIN_PERF_DDR_W_8_52", "DDR 8 bit・52 MHz の最小書込性能コード", "Minimum DDR write performance code (8 bit, 52 MHz)", 5, Needs: Feature.Ddr52, RequireValue: true),
        new(234, "MIN_PERF_DDR_R_8_52", "DDR 8 bit・52 MHz の最小読取性能コード", "Minimum DDR read performance code (8 bit, 52 MHz)", 5, Needs: Feature.Ddr52, RequireValue: true),
        new(232, "TRIM_MULT", "TRIM タイムアウト", "TRIM timeout", 5, Encoding: Format.Time300, Needs: Feature.Trim, RequireValue: true),
        new(231, "SEC_FEATURE_SUPPORT", "セキュア消去機能のビットマップ", "Secure erase features bitmap", 5),
        new(230, "SEC_ERASE_MULT", "セキュア消去の時間倍率", "Secure erase timeout multiplier", 5, Encoding: Format.Count, Needs: Feature.SecureErase, RequireValue: true),
        new(229, "SEC_TRIM_MULT", "セキュア TRIM の時間倍率", "Secure TRIM timeout multiplier", 5, Encoding: Format.Count, Needs: Feature.SecureTrim, RequireValue: true),
        new(228, "BOOT_INFO", "ブート機能のビットマップ", "Boot capabilities bitmap", 3, Needs: Feature.Boot),
        new(225, "ACC_SIZE", "最小アクセス単位コード", "Minimum access unit code", 3, RequireValue: true),
        new(224, "HC_ERASE_GRP_SIZE", "高容量消去グループのサイズ", "High-capacity erase group size", 3, Encoding: Format.KiB512, RequireValue: true),
        new(223, "ERASE_TIMEOUT_MULT", "高容量消去グループのタイムアウト", "High-capacity erase group timeout", 3, Encoding: Format.Time300, RequireValue: true),
        new(222, "REL_WR_SEC_C", "信頼性書き込みのセクター数", "Reliable write sector count", 3, Encoding: Format.Count),
        new(221, "HC_WP_GRP_SIZE", "書き込み保護グループのサイズ", "Write-protect group size", 3, Encoding: Format.GroupSize, RequireValue: true),
        new(220, "S_C_VCC", "スリープ中の VCC 電流コード", "VCC sleep current code", 3, RequireValue: true),
        new(219, "S_C_VCCQ", "スリープ中の VCCQ 電流コード", "VCCQ sleep current code", 3, RequireValue: true),
        new(218, "PRODUCTION_STATE_AWARENESS_TIMEOUT", "生産状態切替タイムアウトコード", "Production state transition timeout code", 7, Needs: Feature.ProductState, RequireValue: true),
        new(217, "S_A_TIMEOUT", "スリープからの状態切替タイムアウトコード", "Sleep state transition timeout code", 3, RequireValue: true),
        new(216, "SLEEP_NOTIFICATION_TIME", "スリープ通知タイムアウト", "Sleep notification timeout", 7, Encoding: Format.Time10, RequireValue: true),
        new(211, "SECURE_WP_INFO", "セキュア書き込み保護の対応と状態", "Secure write protection support and state", 7),
        new(210, "MIN_PERF_W_8_52", "8 bit・52 MHz の最小書込性能コード", "Minimum write performance code (8 bit, 52 MHz)", RequireValue: true),
        new(209, "MIN_PERF_R_8_52", "8 bit・52 MHz の最小読取性能コード", "Minimum read performance code (8 bit, 52 MHz)", RequireValue: true),
        new(208, "MIN_PERF_W_8_26_4_52", "8 bit・26 MHz / 4 bit・52 MHz 最小書込性能コード", "Minimum write performance code (8 bit at 26 MHz or 4 bit at 52 MHz)", RequireValue: true),
        new(207, "MIN_PERF_R_8_26_4_52", "8 bit・26 MHz / 4 bit・52 MHz 最小読取性能コード", "Minimum read performance code (8 bit at 26 MHz or 4 bit at 52 MHz)", RequireValue: true),
        new(206, "MIN_PERF_W_4_26", "4 bit・26 MHz の最小書込性能コード", "Minimum write performance code (4 bit, 26 MHz)", RequireValue: true),
        new(205, "MIN_PERF_R_4_26", "4 bit・26 MHz の最小読取性能コード", "Minimum read performance code (4 bit, 26 MHz)", RequireValue: true),
        new(203, "PWR_CL_26_360", "26 MHz・3.6 V の電力クラス", "26 MHz power class at 3.6 V"),
        new(202, "PWR_CL_52_360", "52 MHz・3.6 V の電力クラス", "52 MHz power class at 3.6 V"),
        new(201, "PWR_CL_26_195", "26 MHz・1.95 V の電力クラス", "26 MHz power class at 1.95 V"),
        new(200, "PWR_CL_52_195", "52 MHz・1.95 V の電力クラス", "52 MHz power class at 1.95 V"),
        new(199, "PARTITION_SWITCH_TIME", "パーティション切替タイムアウト", "Partition switch timeout", 5, Encoding: Format.Time10, RequireValue: true),
        new(198, "OUT_OF_INTERRUPT_TIME", "HPI による割り込み準備時間", "High-priority interrupt response time", 5, Encoding: Format.Time10, Needs: Feature.Hpi, RequireValue: true),
        new(191, "CMD_SET", "現在のコマンドセット", "Current command set", Encoding: Format.Count),
        new(189, "CMD_SET_REV", "コマンドセットの版", "Command set revision", Encoding: Format.Count),
        new(187, "POWER_CLASS", "現在の電力クラス", "Current power class", Encoding: Format.Count),
        new(181, "ERASED_MEM_CONT", "消去済み領域のデータパターン", "Erased memory data pattern", 3, Encoding: Format.ErasedValue),
        new(179, "PARTITION_CONFIG", "ブートとアクセス先の設定", "Boot and partition access configuration", 3),
        new(178, "BOOT_CONFIG_PROT", "ブート設定保護フラグ", "Boot configuration protection flags", 5, Needs: Feature.Boot),
        new(177, "BOOT_BUS_CONDITIONS", "ブート時のバス設定", "Boot bus configuration", 3, Needs: Feature.Boot),
        new(175, "ERASE_GROUP_DEF", "高容量消去グループ定義", "High-capacity erase group definition", 3, Encoding: Format.EnabledBit),
        new(174, "BOOT_WP_STATUS", "ブート領域の書き込み保護状態", "Boot partition write protection status", 5, Needs: Feature.Boot),
        new(173, "BOOT_WP", "ブート領域の書き込み保護設定", "Boot partition write protection configuration", 5, Needs: Feature.Boot),
        new(171, "USER_WP", "ユーザー領域の書き込み保護設定", "User area write protection configuration", 5),
        new(169, "FW_CONFIG", "ファームウェア更新の設定", "Firmware update configuration", 5),
        new(167, "WR_REL_SET", "パーティション別の信頼性書き込み設定", "Reliable write settings by partition", 5),
        new(166, "WR_REL_PARAM", "信頼性書き込みの対応情報", "Reliable write support", 5),
        new(163, "BKOPS_EN", "バックグラウンド処理の設定", "Background operation configuration", 5, Needs: Feature.Background),
        new(162, "RST_n_FUNCTION", "ハードウェアリセット設定", "Hardware reset configuration", 5),
        new(161, "HPI_MGMT", "高優先度割り込み HPI", "High-priority interrupt (HPI)", 5, Encoding: Format.EnabledBit, Needs: Feature.Hpi),
        new(160, "PARTITIONING_SUPPORT", "パーティション機能の対応情報", "Partitioning support", 5),
        new(157, "MAX_ENH_SIZE_MULT", "拡張領域の最大サイズ倍率", "Maximum enhanced area size multiplier", 5, 3, Format.Count, Feature.Enhanced),
        new(156, "PARTITIONS_ATTRIBUTE", "パーティション属性の設定", "Partition attributes", 5, Needs: Feature.Enhanced),
        new(155, "PARTITION_SETTING_COMPLETED", "パーティション設定の確定フラグ", "Partition configuration completion flag", 5, Needs: Feature.Partitions),
        new(143, "GP_SIZE_MULT_GP0", "汎用パーティション 1 のサイズ倍率", "General-purpose partition 1 size multiplier", 5, 3, Format.Count, Feature.Partitions),
        new(146, "GP_SIZE_MULT_GP1", "汎用パーティション 2 のサイズ倍率", "General-purpose partition 2 size multiplier", 5, 3, Format.Count, Feature.Partitions),
        new(149, "GP_SIZE_MULT_GP2", "汎用パーティション 3 のサイズ倍率", "General-purpose partition 3 size multiplier", 5, 3, Format.Count, Feature.Partitions),
        new(152, "GP_SIZE_MULT_GP3", "汎用パーティション 4 のサイズ倍率", "General-purpose partition 4 size multiplier", 5, 3, Format.Count, Feature.Partitions),
        new(140, "ENH_SIZE_MULT", "拡張ユーザー領域のサイズ倍率", "Enhanced user area size multiplier", 5, 3, Format.Count, Feature.Enhanced),
        new(136, "ENH_START_ADDR", "拡張ユーザー領域の開始アドレス", "Enhanced user area start address", 5, 4, Needs: Feature.Enhanced),
        new(134, "SEC_BAD_BLK_MGMNT", "セキュア不良ブロック管理設定", "Secure bad block management setting", 5, Needs: Feature.SecureErase),
        new(133, "PRODUCTION_STATE_AWARENESS", "現在の生産状態", "Current production state", 7, Needs: Feature.ProductState),
        new(132, "TCASE_SUPPORT", "対応温度区分のビットマップ", "Supported case temperature classes bitmap", 6),
        new(131, "PERIODIC_WAKEUP", "スリープ中の定期復帰設定", "Periodic wake-up setting", 6),
        new(130, "PROGRAM_CID_CSD_DDR_SUPPORT", "DDR 時の CID/CSD プログラム", "CID/CSD programming in DDR mode", 6, Encoding: Format.SupportBit),
        new(64, "VENDOR_SPECIFIC_FIELD", "メーカー独自の領域", "Vendor-specific field", 5, 64, Format.Bytes, RequireValue: true),
        new(60, "INI_TIMEOUT_EMU", "セクターエミュレーション切替時の初期化時間コード", "Initialization timeout code for sector size emulation", 6, Needs: Feature.Native4K, RequireValue: true),
        new(59, "CLASS_6_CTRL", "書き込み保護コマンドのモード", "Write protection command mode", 6),
        new(58, "DYNCAP_NEEDED", "解放を要求しているグループ数", "Groups requested for release", 6, Encoding: Format.Count),
        new(56, "EXCEPTION_EVENTS_CTRL", "例外イベントの通知設定", "Exception event notification configuration", 6, 2),
        new(54, "EXCEPTION_EVENTS_STATUS", "例外イベントの発生フラグ", "Exception event status flags", 6, 2),
        new(52, "EXT_PARTITIONS_ATTRIBUTE", "拡張パーティション属性", "Extended partition attributes", 6, 2, Needs: Feature.ExtendedPartitions),
        new(37, "CONTEXT_CONF", "各コンテキストの設定バイト列", "Context configuration bytes", 6, 15, Format.Bytes, Feature.Context),
        new(36, "PACKED_COMMAND_STATUS", "一括コマンドのステータス", "Packed command status", 6, Needs: Feature.Packed),
        new(35, "PACKED_FAILURE_INDEX", "失敗した一括コマンドの位置", "Index of the failed packed command", 6, Needs: Feature.Packed, RequireValue: true),
        new(34, "POWER_OFF_NOTIFICATION", "電源オフ通知設定", "Power-off notification configuration", 6),
        new(33, "CACHE_CTRL", "キャッシュ", "Cache", 6, Encoding: Format.EnabledBit, Needs: Feature.Cache),
        new(31, "BARRIER_CTRL", "キャッシュバリアの使用設定", "Cache barrier configuration", 8, Encoding: Format.EnabledBit, Needs: Feature.Barrier),
        new(30, "MODE_CONFIG", "現在のデバイス動作モード", "Current device operating mode", 7),
        new(26, "FFU_STATUS", "ファームウェア更新の結果コード", "Firmware update result code", 7, Needs: Feature.Firmware),
        new(22, "PRE_LOADING_DATA_SIZE", "設定済みのプリロードセクター数", "Configured preloaded sector count", 7, 4, Format.Count, Feature.ProductState),
        new(18, "MAX_PRE_LOADING_DATA_SIZE", "プリロード可能なセクター数", "Maximum preloaded sector count", 7, 4, Format.Count, Feature.ProductState),
        new(17, "PRODUCT_STATE_AWARENESS_ENABLEMENT", "生産状態管理の対応と設定", "Production state management support and configuration", 7),
        new(16, "SECURE_REMOVAL_TYPE", "セキュア消去方式の対応と設定", "Secure removal type support and configuration", 7, Needs: Feature.SecureErase)
    ];

    private static readonly string[] CardModes =
        ["HS26", "HS52", "DDR52 1.8/3 V", "DDR52 1.2 V", "HS200 1.8 V", "HS200 1.2 V", "HS400 1.8 V", "HS400 1.2 V"];

    internal static bool IsKnownRevision(byte revision) => revision <= 9;

    internal static EmmcOverview Overview(byte[] data, UiText text)
    {
        byte revision = data[192];
        string specification = RevisionName(revision, text);
        if (!IsKnownRevision(revision))
            return new(specification, null, null, null, text.Pick("不明", "Unknown"));

        ReadOnlySpan<byte> firmwareBytes = data.AsSpan(254, 8);
        string? firmware = revision >= 7 && firmwareBytes.ContainsAnyExcept((byte)0)
            ? FirmwareText(firmwareBytes) ?? "0x" + Convert.ToHexString(firmwareBytes)
            : null;
        return new(specification, firmware, TransferMode(data, revision, text),
            revision >= 6 ? CacheStatus(data, text) : null, QueueStatus(data, revision, text));
    }

    internal static List<RegisterRow> Rows(byte[] data, UiText text)
    {
        byte revision = data[192];
        List<RegisterRow> rows = [new("EXT_CSD_REV", "192", null, Hex(revision), RevisionName(revision, text))];
        if (!IsKnownRevision(revision))
            return rows;

        foreach (Field field in Fields)
        {
            if (field.Since > revision || !Supports(data, field.Needs))
                continue;
            ReadOnlySpan<byte> bytes = data.AsSpan(field.Offset, field.Length);
            bool orderedBytes = field.Encoding is Format.Bytes or Format.Firmware;
            ulong number = orderedBytes ? 0 : LittleEndian(bytes);
            if (field.RequireValue && (orderedBytes ? !bytes.ContainsAnyExcept((byte)0) : number == 0))
                continue;
            string address = field.Length == 1 ? field.Offset.ToString() : $"{field.Offset}–{field.Offset + field.Length - 1}";
            string raw = orderedBytes ? Convert.ToHexString(bytes) : Hex(number, field.Length * 2);
            rows.Add(new(field.Id, address, null, raw, Explain(field, number, bytes, data, text)));
        }
        return rows;
    }

    private static string RevisionName(byte revision, UiText text) => revision switch
    {
        0 => "MMC 4.0", 1 => "MMC 4.1", 2 => "MMC 4.2", 3 => "MMC 4.3",
        4 => text.Pick("リビジョン 1.4（廃版）", "Revision 1.4 (obsolete)"),
        5 => "eMMC 4.41", 6 => "eMMC 4.5 / 4.51",
        7 => "eMMC 5.0 / 5.01", 8 => "eMMC 5.1", 9 => "eMMC 5.1B",
        _ => text.Pick($"未対応のリビジョン {revision}", $"Unsupported revision {revision}")
    };

    private static bool Supports(byte[] data, Feature feature) => feature switch
    {
        Feature.Queue => (data[308] & 1) != 0,
        Feature.Barrier => (data[486] & 1) != 0,
        Feature.Firmware => (data[493] & 1) != 0,
        Feature.FirmwareInstall => (data[493] & data[492] & 1) != 0,
        Feature.Cache => LittleEndian(data.AsSpan(249, 4)) != 0,
        Feature.Background => (data[502] & 1) != 0,
        Feature.Hpi => (data[503] & 1) != 0,
        Feature.Packed => data[500] != 0 || data[501] != 0,
        Feature.Tag => (data[499] & 1) != 0,
        Feature.Context => (data[496] & 15) != 0,
        Feature.ExtendedPartitions => (data[494] & 3) != 0,
        Feature.Partitions => (data[160] & 1) != 0,
        Feature.Enhanced => (data[160] & 2) != 0,
        Feature.Boot => data[226] != 0,
        Feature.ProductState => (data[17] & 1) != 0,
        Feature.SecurityError => (data[54] & 0x10) != 0,
        Feature.SecureErase => (data[231] & 1) != 0,
        Feature.SecureTrim => (data[231] & 0x10) != 0 && (data[231] & 1) != 0,
        Feature.Trim => (data[231] & 0x10) != 0,
        Feature.Ddr52 => (data[196] & 0x0C) != 0,
        Feature.Hs200 => (data[196] & 0xF0) != 0,
        Feature.Hs400 => (data[196] & 0xC0) != 0,
        Feature.Native4K => data[63] == 1,
        _ => true
    };

    private static string Explain(Field field, ulong number, ReadOnlySpan<byte> bytes, byte[] data, UiText text)
    {
        string name = text.Pick(field.Japanese, field.English);
        string reserved = text.Pick("予約値", "Reserved");
        string byteUnit = text.Pick("バイト", "bytes");
        return field.Encoding switch
        {
            Format.Count => $"{name} {number:N0}",
            Format.Sectors512 => $"{number:N0} × 512 {byteUnit}",
            Format.KiB => $"{name} {number:N0} KiB",
            Format.KiB4 => $"{name} {number * 4:N0} KiB",
            Format.KiB128 => $"{name} {number * 128:N0} KiB",
            Format.KiB512 => $"{name} {number * 512:N0} KiB",
            Format.GroupSize when data[224] != 0 => $"{name} {number * data[224] * 512:N0} KiB",
            Format.GroupSize => $"{name} {number:N0} {text.Pick("消去グループ", "erase groups")}",
            Format.Time10 => $"{name} {number * 10:N0} ms",
            Format.Time300 => $"{name} {number * 300:N0} ms",
            Format.SupportBit => $"{name} {((number & 1) != 0 ? text.Pick("対応", "supported") : text.Pick("非対応", "unsupported"))}",
            Format.EnabledBit => $"{name} {Enabled(number, text)}",
            Format.QueueDepth => text.Pick($"最大 {QueueDepth(number)} コマンド", $"Up to {QueueDepth(number)} commands"),
            Format.SectorSize => number switch { 0 => $"512 {byteUnit}", 1 => $"4,096 {byteUnit}", _ => reserved },
            Format.CardType when number != 0 => string.Join(" / ", CardModes.Where((_, bit) => (number & (1UL << bit)) != 0)),
            Format.Timing => Timing(number, text),
            Format.BusWidth => BusWidth(number, text),
            Format.ErasedValue => number switch { 0 => "0x00", 1 => "0xFF", _ => reserved },
            Format.BackgroundStatus => number switch
            {
                0 => text.Pick("処理不要", "No operations needed"),
                1 => text.Pick("処理あり", "Operations pending"),
                2 => text.Pick("処理が必要", "Operations needed"),
                3 => text.Pick("至急処理が必要", "Urgent operations needed"),
                _ => reserved
            },
            Format.Bytes => text.Pick($"{name}（{bytes.Length} バイト）", $"{name} ({bytes.Length} bytes)"),
            Format.Firmware => FirmwareText(bytes) ?? text.Pick("ファームウェア版のバイト列", "Firmware version bytes"),
            _ => name
        };
    }

    private static string? TransferMode(byte[] data, byte revision, UiText text)
    {
        int widthCode = data[183] & 15;
        string? current = (data[185] & 15) switch
        {
            0 => text.Pick("互換モード", "Legacy"),
            1 when revision >= 5 && widthCode is 5 or 6 => "DDR52",
            1 => "High Speed",
            2 when revision >= 6 => "HS200",
            3 when revision >= 7 => "HS400",
            _ => null
        };
        string? maximum = data[196] switch
        {
            var mask when revision >= 7 && (mask & 0xC0) != 0 => "HS400",
            var mask when revision >= 6 && (mask & 0x30) != 0 => "HS200",
            var mask when revision >= 5 && (mask & 0x0C) != 0 => "DDR52",
            var mask when (mask & 2) != 0 => "HS52",
            var mask when (mask & 1) != 0 => "HS26",
            _ => null
        };
        if (current is null)
            return maximum is null ? null : text.Pick($"現在不明 / 最大 {maximum}", $"Unknown / up to {maximum}");
        if (widthCode is 0 or 1 or 2 || revision >= 5 && widthCode is 5 or 6)
        {
            string width = BusWidth(revision >= 8 ? data[183] : (uint)widthCode, text);
            current += text.Pick($"（{width}）", $" ({width})");
        }
        return maximum is null ? current : text.Pick($"{current} / 最大 {maximum}", $"{current} / up to {maximum}");
    }

    private static string CacheStatus(byte[] data, UiText text)
    {
        ulong capacity = LittleEndian(data.AsSpan(249, 4));
        return capacity == 0 ? text.Pick("0 KiB（非対応）", "0 KiB (unsupported)") : $"{capacity:N0} KiB / {Enabled(data[33], text)}";
    }

    private static string QueueStatus(byte[] data, byte revision, UiText text)
    {
        if (revision < 8 || !Supports(data, Feature.Queue))
            return text.Pick("非対応 / 非適用", "Unsupported / inactive");
        string mode = (data[15] & 1) != 0 ? text.Pick("適用", "active") : text.Pick("非適用", "inactive");
        return text.Pick($"対応 / {mode}（最大 {QueueDepth(data[307])} 件）", $"Supported / {mode} (up to {QueueDepth(data[307])})");
    }

    private static string Enabled(ulong value, UiText text) =>
        (value & 1) != 0 ? text.Pick("有効", "Enabled") : text.Pick("無効", "Disabled");

    private static ulong QueueDepth(ulong value) => (value & 31) + 1;

    private static string BusWidth(ulong number, UiText text)
    {
        string width = (number & 15) switch
        {
            0 => "1 bit", 1 => "4 bit SDR", 2 => "8 bit SDR", 5 => "4 bit DDR", 6 => "8 bit DDR", _ => text.Pick("予約値", "Reserved")
        };
        return (number & 0x80) != 0 ? text.Pick($"{width} / 拡張ストローブ", $"{width} / enhanced strobe") : width;
    }

    private static string Timing(ulong number, UiText text)
    {
        string timing = (number & 15) switch
        {
            0 => text.Pick("互換タイミング", "Legacy timing"), 1 => text.Pick("ハイスピード", "High Speed"),
            2 => "HS200", 3 => "HS400", _ => text.Pick("予約値", "Reserved")
        };
        return text.Pick($"{timing} / ドライブ強度 {number >> 4}", $"{timing} / driver strength {number >> 4}");
    }

    private static string? FirmwareText(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty && bytes[^1] is 0 or 0x20)
            bytes = bytes[..^1];
        if (bytes.IsEmpty)
            return null;
        foreach (byte value in bytes)
            if (value is < 0x20 or > 0x7E)
                return null;
        return System.Text.Encoding.ASCII.GetString(bytes);
    }

    private static ulong LittleEndian(ReadOnlySpan<byte> bytes)
    {
        ulong result = 0;
        for (int i = bytes.Length - 1; i >= 0; i--)
            result = (result << 8) | bytes[i];
        return result;
    }

    private static string Hex(ulong value, int digits = 2) => "0x" + value.ToString("X" + digits);
}
