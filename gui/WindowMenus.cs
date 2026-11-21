using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static SdmmcInspector.Win32;

namespace SdmmcInspector;

internal sealed partial class MainWindow
{
    private const int SystemLanguageId = 170, JapaneseLanguageId = 171, EnglishLanguageId = 172;
    private const int SaveExtCsdId = 181, SaveReportId = 182, SaveImageId = 183;
    private const int CopyReportId = 184, CopyRowId = 185, HideSerialId = 186;
    private const int DiskManagementId = 187, DeviceManagerId = 188, HelpId = 189, AboutId = 190;
    private const int FirstDeviceId = 2000;

    private nint _fileMenu, _editMenu, _operationMenu, _viewMenu, _deviceMenu, _languageMenu;

    private nint CreateMenus()
    {
        nint menu = CreateMenu();
        if (menu == 0)
            throw new InvalidOperationException(Pick("メニューバーを作成できませんでした。", "Could not create the menu bar."));

        try
        {
            _fileMenu = AddPopup(menu, Pick("ファイル(&F)", "&File"),
                (SaveExtCsdId, Pick("EXT_CSD を書き出す（512 バイト）(&E)…\tCtrl+E", "&Export EXT_CSD (512 bytes)…\tCtrl+E")),
                (SaveReportId, Pick("レポートを保存（テキスト）(&T)…\tCtrl+T", "Save Report (&Text)…\tCtrl+T")),
                (SaveImageId, Pick("画面を保存（PNG）(&S)…\tCtrl+S", "&Save Image (PNG)…\tCtrl+S")), (0, null),
                (ExitId, Pick("終了(&X)\tAlt+F4", "E&xit\tAlt+F4")));
            _editMenu = AddPopup(menu, Pick("編集(&E)", "&Edit"),
                (CopyReportId, Pick("レポートをコピー(&R)\tCtrl+Shift+C", "Copy &Report\tCtrl+Shift+C")),
                (CopyRowId, Pick("選択したレジスタをコピー(&C)\tCtrl+C", "&Copy Selected Register\tCtrl+C")));
            _operationMenu = AddPopup(menu, Pick("機能(&U)", "&Tools"),
                (ScanId, Pick("再検出(&S)\tF5", "Re&scan Devices\tF5")),
                (ReadId, Pick("読み取り(&R)\tCtrl+R", "&Refresh Selected Device\tCtrl+R")),
                (SdId, Pick("SD レジスタ読取（試験）(&D)", "Read S&D Registers (Experimental)")), (0, null),
                (DiskManagementId, Pick("ディスクの管理を開く(&K)", "Open Dis&k Management")),
                (DeviceManagerId, Pick("デバイス マネージャーを開く(&M)", "Open Device &Manager")));
            _viewMenu = AddPopup(menu, Pick("表示(&V)", "&View"),
                (TableId, Pick("詳細データ(&T)", "&Register Table")),
                (RawId, Pick("生データ(&B)\tCtrl+B", "Raw &Bytes\tCtrl+B")), (0, null),
                (HideSerialId, Pick("シリアル番号を隠す(&N)", "Hide &Serial Number")));
            _deviceMenu = AddPopup(menu, Pick("機器(&D)", "&Device"));
            nint settings = AddPopup(menu, Pick("設定(&S)", "&Settings"),
                (SettingsId, Pick("寿命判定のしきい値(&L)…", "&Lifetime Thresholds…")));
            AddRefreshMenus(settings);
            _languageMenu = AddPopup(settings, Pick("言語(&G)", "Lan&guage"),
                (SystemLanguageId, Pick("Windows の設定に従う(&W)", "Use &Windows Language")),
                (JapaneseLanguageId, "日本語 (&J)"), (EnglishLanguageId, "&English"));
            UpdateLanguageChecks();
            AddPopup(menu, Pick("ヘルプ(&H)", "&Help"),
                (HelpId, Pick("使い方(&H)\tF1", "&How to Use\tF1")),
                (AboutId, Pick("SDMMC Inspector について(&A)", "&About SDMMC Inspector")));
            return menu;
        }
        catch
        {
            DestroyMenu(menu);
            throw;
        }
    }

    private nint AddPopup(nint parent, string caption, params (uint Id, string? Text)[] items)
    {
        nint popup = CreatePopupMenu();
        if (popup == 0 || AppendMenu(parent, MfPopup, (nuint)popup, caption) == 0)
        {
            if (popup != 0)
                DestroyMenu(popup);
            throw new InvalidOperationException(Pick("ドロップダウンメニューを作成できませんでした。",
                "Could not create the drop-down menu."));
        }
        foreach (var item in items)
            AddMenuItem(popup, item.Id, item.Text);
        return popup;
    }

    private void AddMenuItem(nint menu, uint id, string? caption, uint flags = 0)
    {
        if (AppendMenu(menu, caption is null ? MfSeparator : flags, id, caption) == 0)
            throw new InvalidOperationException(Pick("メニュー項目を追加できませんでした。", "Could not add the menu item."));
    }

    private void PopulateDeviceMenu(int selectedIndex)
    {
        int count = GetMenuItemCount(_deviceMenu);
        for (int i = count - 1; i >= 0; i--)
            DeleteMenu(_deviceMenu, (uint)i, MfByPosition);
        if (_targets.Count == 0)
            AddMenuItem(_deviceMenu, 0, Pick("選択できる機器がありません", "No devices available"), MfGrayed);
        for (int i = 0; i < _targets.Count; i++)
        {
            string title = _targets[i].MenuLabel(_text).Replace("&", "&&")
                .Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            AddMenuItem(_deviceMenu, (uint)(FirstDeviceId + i), title, i == selectedIndex ? MfChecked : 0);
        }
    }

    private void UpdateLanguageChecks()
    {
        uint selected = _preferences.Language switch
        {
            AppLanguage.Japanese => JapaneseLanguageId,
            AppLanguage.English => EnglishLanguageId,
            _ => SystemLanguageId
        };
        CheckMenuRadioItem(_languageMenu, SystemLanguageId, EnglishLanguageId, selected, 0);
    }

    private void ChangeLanguage(AppLanguage language)
    {
        if (_preferences.Language != language && TrySavePreferences(_preferences with { Language = language }))
            ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        UiText language = UiText.Resolve(_preferences.Language, GetUserDefaultUILanguage());
        if (_text == language)
        {
            UpdateLanguageChecks();
            return;
        }

        int selection = (int)SendMessage(_combo, CbGetCurSel, 0, 0);
        _text = language;
        nint previous = GetMenu(_window);
        nint replacement = CreateMenus();
        if (SetMenu(_window, replacement) == 0)
        {
            DestroyMenu(replacement);
            throw new Win32Exception(Marshal.GetLastPInvokeError(),
                Pick("メニューバーを更新できませんでした。", "Could not update the menu bar."));
        }
        DestroyMenu(previous);
        DrawMenuBar(_window);
        PopulateCombo(selection);
        LocalizeControls();
        ApplyFonts();
        Layout();
        RenderReport(languageChanged: true);
    }

    private void ToggleSerial()
    {
        if (!TrySavePreferences(_preferences with { HideSerial = !_preferences.HideSerial }))
            return;
        RenderDeviceFields();
        UpdateControls();
    }

    private bool TrySavePreferences(AppPreferences changes)
    {
        try
        {
            changes.Save();
            _preferences = changes;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox(_window, Pick("設定を保存できませんでした。設定フォルダーへのアクセスを確認してください。",
                "Could not save settings. Check access to the settings folder."), AppName, 0x10);
            return false;
        }
    }

    private void OpenWindowsTool(string name)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, name))
            {
                UseShellExecute = true
            });
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            // The user dismissed the Windows elevation prompt.
        }
        catch (Win32Exception error)
        {
            MessageBox(_window, Pick($"Windows の管理ツールを開けませんでした (Win32 {error.NativeErrorCode})。",
                $"Could not open the Windows management tool (Win32 {error.NativeErrorCode})."), AppName, 0x10);
        }
    }

    private void ShowHelp() => MessageBox(_window, Pick(
        "機器を選ぶと、取得できた寿命情報とレジスタを表示します。\r\n\r\n" +
        "読み直すには Ctrl+R、機器の抜き差し後は F5 を使います。\r\n" +
        "レジスタの行をダブルクリックすると詳細を確認できます。\r\n" +
        "eMMC の EXT_CSD を取得できた場合は、ファイルから 512 バイトを書き出せます。\r\n\r\n" +
        "ネイティブ SD の試験読取には対応ドライバーが必要です。アクセスが拒否された場合は、管理者として起動してください。",
        "Select a device to view its available lifetime data and registers.\r\n\r\n" +
        "Press Ctrl+R to read it again or F5 to rescan after reconnecting a device.\r\n" +
        "Double-click a register row to view its details.\r\n" +
        "When EXT_CSD is available, use File to export all 512 bytes.\r\n\r\n" +
        "Experimental native SD reads require a compatible driver. If access is denied, try running as administrator."),
        Pick("使い方", "How to Use"), 0x40);

    private void ShowAbout()
    {
        string? version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        MessageBox(_window, $"{AppName} {version}\r\n\r\n" +
            Pick("Windows x64 用の eMMC・SD レジスタ読み取りツール", "eMMC and SD register reader for Windows x64") +
            "\r\nminiai-saka", Pick("SDMMC Inspector について", "About SDMMC Inspector"), 0x40);
    }

    private bool HandleShortcut(Message message)
    {
        if (message.Id != WmKeyDown)
            return false;
        if (message.WParam == 0x70) // F1
        {
            ShowHelp();
            return true;
        }
        if (message.WParam == 0x74) // F5
        {
            StartScan();
            return true;
        }
        if (message.Hwnd == _table && message.WParam == 0x0D) // Enter
        {
            ShowSelectedRegister();
            return true;
        }
        if (GetKeyState(0x11) >= 0) // Control
            return false;

        switch (message.WParam)
        {
            case 'R': StartRead(probe: false); return true;
            case 'B': ChangeDataView(!_rawVisible); return true;
            case 'E': ExportExtCsd(); return true;
            case 'T': ExportReport(); return true;
            case 'S': ExportImage(); return true;
            case 'C' when GetKeyState(0x10) < 0: CopyReport(); return true; // Shift
            case 'C' when message.Hwnd == _table: CopySelectedRegister(); return true;
            case 'A' when message.Hwnd == _raw: SendMessage(_raw, EmSetSel, 0, -1); return true;
            default: return false;
        }
    }
}
