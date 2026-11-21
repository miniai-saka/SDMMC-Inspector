using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using static SdmmcInspector.Win32;

namespace SdmmcInspector;

internal sealed partial class MainWindow
{
    private enum LabelRole
    {
        Small, StripLabel, StripSmall, LifeLabel, LifeState, LifeDetail,
        DeviceTitle, DeviceLabel, DeviceValue, Section, EmptyTable, Raw
    }

    private readonly record struct Tone(uint Ink, uint Surface);

    private const int Margin = 14, StripHeight = 43, SectionTop = 302, TableTop = 333, TimeSlot = 113;
    private const int LifeLeft = 17, LifeTop = 79, LifePitch = 64;
    private const int MetadataTop = 96, MetadataPitch = 20, MetadataHeight = 19;
    private const int FirmwareRow = 5;

    private static readonly (string Japanese, string English)[] DeviceFields =
        [("容量", "Capacity"), ("インターフェース", "Interface"), ("転送モード", "Transfer mode"),
         ("キャッシュ容量", "Cache"), ("コマンドキュー", "Command queue"), ("ファームウェア", "Firmware"),
         ("シリアル番号", "Serial number"), ("メーカー", "Vendor"), ("物理ディスク", "Physical disk"),
         ("ドライブ", "Drive letters")];
    private static readonly (string Japanese, string English)[] RegisterColumns =
        [("項目", "Register"), ("位置", "Offset"), ("判定", "Status"), ("生値", "Raw value"), ("内容", "Description")];

    private static readonly uint Paper = Color(0xFFFFFF), Accent = Color(0xEBF5FF), Line = Color(0xB9CEE1);
    private static readonly uint Ink = Color(0x192936), Muted = Color(0x405569);

    private readonly Dictionary<nint, (LabelRole Role, int Index)> _labelRoles = [];
    private readonly Dictionary<uint, nint> _brushes = [];
    private readonly nint[] _metadataLabels = new nint[DeviceFields.Length], _metadataValues = new nint[DeviceFields.Length];
    private readonly (nint Label, nint Status, nint Detail)[] _lifeLabels = new (nint, nint, nint)[3];
    private readonly Health[] _lifeHealth = [Health.Unknown, Health.Unknown, Health.Unknown];
    private nint _selector, _combo, _lifeHeading, _product, _section;
    private nint _table, _raw, _empty, _notice, _time;
    private nint _bodyFont, _smallFont, _productFont, _sectionFont, _healthFont, _monoFont, _rowHeight;
    private IReadOnlyList<RegisterRow> _shownRows = [];
    private string _shownRaw = "";
    private int _width, _height;
    private bool _highContrast;
    private int LifeWidth => _text.English ? 170 : 150;
    private int MetadataLeft => LifeLeft + LifeWidth + 21;
    private int MetadataValueLeft => _text.English ? 352 : 328;
    private string UiFont => _text.English ? "Segoe UI" : "Yu Gothic UI";

    private void CreateControls()
    {
        _selector = Label("", LabelRole.StripLabel);
        _combo = Child("ComboBox", "", WsTabStop | WsVisible | WsVScroll | 0x0003 | 0x0200, ComboId);
        _lifeHeading = Label("", LabelRole.Section);
        for (int i = 0; i < _lifeLabels.Length; i++)
        {
            _lifeLabels[i] = (Label("", LabelRole.LifeLabel),
                Label("", LabelRole.LifeState, index: i),
                Label("—", LabelRole.LifeDetail, align: 2));
        }

        _product = Label("", LabelRole.DeviceTitle, align: 1);
        for (int index = 0; index < DeviceFields.Length; index++)
        {
            _metadataLabels[index] = Label("", LabelRole.DeviceLabel, align: 2);
            _metadataValues[index] = Label("", LabelRole.DeviceValue);
        }

        _section = Label("", LabelRole.Section);
        _table = Child("SysListView32", "",
            WsVisible | WsTabStop | WsBorder | 0x0001 | 0x0004 | 0x0008 | 0x0040 | 0x8000);
        SendMessage(_table, CcmSetVersion, 5, 0);
        SendMessage(_table, LvSetExtendedStyle, 0, (nint)(LvExFullRowSelect | LvExGridLines | LvExInfoTip | LvExDoubleBuffer));
        for (int index = 0; index < RegisterColumns.Length; index++)
            SetRegisterColumn(index, insert: true);

        _raw = Child("Edit", "", WsTabStop | WsBorder | WsHScroll | WsVScroll | 0x0004 | 0x0040 | 0x0080 | 0x0800);
        _labelRoles.Add(_raw, (LabelRole.Raw, 0));
        _empty = Label("", LabelRole.EmptyTable);
        _notice = Label("", LabelRole.Small, align: 2);
        _time = Label("", LabelRole.StripSmall, align: 2);

        LocalizeControls(updateColumns: false);
        ApplyFonts();
        ApplySystemColors();
    }

    private void LocalizeControls(bool updateColumns = true)
    {
        SetWindowText(_selector, Pick("接続機器", "Device"));
        SetWindowText(_lifeHeading, Pick("寿命ステータス", "Lifetime Status"));
        SetWindowText(_table, Pick("レジスタ一覧", "Registers"));
        for (int i = 0; i < _lifeLabels.Length; i++)
            SetWindowText(_lifeLabels[i].Label, _text.LifeName(i));
        if (updateColumns)
        {
            for (int i = 0; i < RegisterColumns.Length; i++)
                SetRegisterColumn(i, insert: false);
        }
    }

    private unsafe void SetRegisterColumn(int index, bool insert)
    {
        var (japanese, english) = RegisterColumns[index];
        fixed (char* name = Pick(japanese, english))
        {
            ListColumn column = new() { Mask = insert ? 0xFu : 5u, Text = name, Width = 100, SubItem = index };
            SendMessage(_table, insert ? LvInsertColumn : LvSetColumn, (nuint)index, (nint)(&column));
        }
    }

    private nint Child(string className, string text, uint style, int id = 0)
    {
        nint control = CreateWindowEx(0, className, text, style | WsChild, 0, 0, 0, 0, _window, id, _module, 0);
        if (control == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(),
                Pick($"画面要素を作成できませんでした ({className})。", $"Could not create the {className} control."));
        return control;
    }

    private nint Label(string text, LabelRole role, uint align = 0, int index = 0)
    {
        nint control = Child("Static", text, WsVisible | 0x80 | 0x200 | 0x4000 | align);
        _labelRoles.Add(control, (role, index));
        return control;
    }

    private int Scale(int dip) => (dip * _dpi + 48) / 96;

    private unsafe void Layout()
    {
        Rect client = default;
        GetClientRect(_window, &client);
        _width = client.Width * 96 / _dpi;
        _height = client.Height * 96 / _dpi;

        int right = _width - Margin;
        Place(_selector, Margin, 7, 69, 28);
        const int comboLeft = 87;
        int comboWidth = Math.Max(100, right - TimeSlot - comboLeft);
        Place(_combo, comboLeft, 7, comboWidth, 245);
        SendMessage(_combo, CbSetDroppedWidth, (nuint)Scale(comboWidth), 0);
        Place(_time, right - 103, 7, 103, 28);

        Place(_lifeHeading, LifeLeft, 51, LifeWidth, 24);
        int statusWidth = _text.English ? 88 : 61;
        for (int i = 0; i < _lifeLabels.Length; i++)
        {
            var labels = _lifeLabels[i];
            int y = LifeTop + LifePitch * i;
            Place(labels.Label, LifeLeft, y, LifeWidth, 20);
            Place(labels.Status, LifeLeft + 10, y + 23, statusWidth, 31);
            Place(labels.Detail, LifeLeft + 10 + statusWidth, y + 23, LifeWidth - 19 - statusWidth, 31);
        }

        Place(_product, MetadataLeft, 52, right - MetadataLeft, 36);
        for (int index = 0; index < _metadataLabels.Length; index++)
        {
            int y = MetadataTop + index * MetadataPitch;
            Place(_metadataLabels[index], MetadataLeft, y, MetadataValueLeft - MetadataLeft - 17, MetadataHeight);
            Place(_metadataValues[index], MetadataValueLeft, y, right - MetadataValueLeft - 4, MetadataHeight);
        }

        Place(_section, Margin, SectionTop, 260, 26);
        Place(_notice, right - 220, SectionTop, 220, 26);
        int tableHeight = Math.Max(40, _height - TableTop - Margin);
        Place(_table, Margin, TableTop, right - Margin, tableHeight);
        Place(_raw, Margin, TableTop, right - Margin, tableHeight);
        Place(_empty, Margin + 13, TableTop + 33, right - Margin - 29, 30);
        ResizeColumns();
        InvalidateRect(_window, null, 0);
    }

    private void Place(nint control, int x, int y, int width, int height)
    {
        int left = Scale(x), top = Scale(y);
        MoveWindow(control, left, top, Math.Max(0, Scale(x + width) - left), Math.Max(0, Scale(y + height) - top), 1);
    }

    private void ResizeColumns()
    {
        int[] widths = [_width < 640 ? 189 : 214, 77, _text.English ? 72 : 52, 101];
        int used = 0;
        for (int index = 0; index < widths.Length; index++)
        {
            int width = Scale(widths[index]);
            SendMessage(_table, LvSetColumnWidth, (nuint)index, width);
            used += width;
        }
        int available = Scale(_width - 2 * Margin - 20); // Borders and vertical scrollbar.
        SendMessage(_table, LvSetColumnWidth, 4, Math.Max(Scale(94), available - used));
    }

    private void ApplyFonts()
    {
        nint[] oldFonts = Fonts();
        _bodyFont = Font(UiFont, 14);
        _smallFont = Font(UiFont, 13);
        _productFont = Font(UiFont, 25, 650);
        _sectionFont = Font(UiFont, 17, 650);
        _healthFont = Font(UiFont, _text.English ? 18 : 22, 700);
        _monoFont = Font("Consolas", 15);

        foreach (var (control, label) in _labelRoles)
        {
            nint font = label.Role switch
            {
                LabelRole.LifeState => _healthFont,
                LabelRole.DeviceTitle => _productFont,
                LabelRole.Section => _sectionFont,
                LabelRole.Raw => _monoFont,
                LabelRole.Small or LabelRole.StripSmall or LabelRole.LifeDetail => _smallFont,
                _ => _bodyFont
            };
            SetFont(control, font);
        }
        SetFont(_combo, _bodyFont);
        SetFont(_table, _bodyFont);

        SendMessage(_combo, CbSetItemHeight, unchecked((nuint)(-1)), Scale(22));
        SendMessage(_combo, CbSetItemHeight, 0, Scale(26));
        SendMessage(_raw, EmSetMargins, 3, Scale(11) | (Scale(11) << 16));

        nint heightList = ImageListCreate(1, Scale(22), 0x20, 1, 1);
        SendMessage(_table, LvSetImageList, 1, heightList);
        if (_rowHeight != 0)
            ImageListDestroy(_rowHeight);
        _rowHeight = heightList;
        foreach (nint font in oldFonts)
            if (font != 0)
                DeleteObject(font);
    }

    private nint[] Fonts() => [_bodyFont, _smallFont, _productFont, _sectionFont, _healthFont, _monoFont];

    private nint Font(string family, int size, int weight = 400) =>
        CreateFont(-Scale(size), 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, family);

    private static void SetFont(nint control, nint font) => SendMessage(control, WmSetFont, (nuint)font, 1);

    private void FreeDrawingResources()
    {
        foreach (nint brush in _brushes.Values)
            DeleteObject(brush);
        foreach (nint font in Fonts())
            if (font != 0)
                DeleteObject(font);
        if (_rowHeight != 0)
            ImageListDestroy(_rowHeight);
    }

    private unsafe void ApplySystemColors()
    {
        HighContrast contrast = new() { Size = (uint)sizeof(HighContrast) };
        _highContrast = SystemParametersInfo(0x42, contrast.Size, &contrast, 0) != 0 && (contrast.Flags & 1) != 0;
        SendMessage(_table, LvSetBkColor, 0, (nint)Surface);
        SendMessage(_table, LvSetTextBkColor, 0, (nint)Surface);
        SendMessage(_table, LvSetTextColor, 0, (nint)Foreground);
        InvalidateRect(_window, null, 1);
        foreach (nint child in _labelRoles.Keys)
            InvalidateRect(child, null, 1);
        InvalidateRect(_table, null, 1);
    }

    private unsafe void PopulateCombo(int selectedIndex)
    {
        SendMessage(_combo, CbResetContent, 0, 0);
        foreach (Device device in _targets)
        {
            fixed (char* name = device.MenuLabel(_text))
                SendMessage(_combo, CbAddString, 0, (nint)name);
        }
        if (_targets.Count == 0)
        {
            fixed (char* empty = Pick("選択できる機器がありません", "No devices available"))
                SendMessage(_combo, CbAddString, 0, (nint)empty);
            selectedIndex = 0;
        }
        if (selectedIndex >= 0)
            SendMessage(_combo, CbSetCurSel, (nuint)selectedIndex, 0);
        PopulateDeviceMenu(selectedIndex);
    }

    private string LifeDetail(int index) => _report.Indicators?[index].Detail
        ?? (_device?.Protocol == CardProtocol.Sd ? Pick("対象外", "N/A") : "—");

    private void Render(bool languageChanged = false)
    {
        for (int i = 0; i < _lifeLabels.Length; i++)
        {
            HealthIndicator? indicator = _report.Indicators?[i];
            Health state = indicator?.State ?? Health.Unknown;
            _lifeHealth[i] = state;
            SetWindowText(_lifeLabels[i].Status, _text.HealthName(state));
            SetWindowText(_lifeLabels[i].Detail, LifeDetail(i));
        }
        SetWindowText(_product, _device is null
            ? _report.Summary
            : DisplayOr(_device.Product, _device.Target));
        RenderDeviceFields();
        SetWindowText(_empty, _report.Detail);
        SetWindowText(_notice, _report.Notice ?? "");
        SetWindowText(_time, _updated is { } time
            ? Pick("更新 ", "Updated ") + time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "");

        PopulateTable(languageChanged);
        if (_report.RawText != _shownRaw)
        {
            _shownRaw = _report.RawText;
            SetWindowText(_raw, _shownRaw);
            SendMessage(_raw, EmSetSel, 0, 0);
        }
        if (_shownRaw.Length == 0)
            _rawVisible = false;
        ShowDataView();
        RedrawHealth();
    }

    private void RenderDeviceFields()
    {
        var metadata = DeviceMetadata();
        for (int i = 0; i < metadata.Length; i++)
        {
            SetWindowText(_metadataLabels[i], metadata[i].Label);
            SetWindowText(_metadataValues[i], metadata[i].Value);
        }
    }

    private (string Label, string Value)[] DeviceMetadata()
    {
        EmmcOverview? emmc = _report.Emmc;
        bool nativeFirmware = _device is null or { Protocol: CardProtocol.Mmc } or { BusType: 13 };
        ulong size = _device is { SizeBytes: > 0 } deviceWithSize ? deviceWithSize.SizeBytes : _report.RegisterCapacity;
        string? serial = _device?.Serial;
        if (_preferences.HideSerial && !string.IsNullOrEmpty(serial))
            serial = Pick("非表示", "Hidden");
        string?[] values =
        [
            _device is null ? null : FormatCapacity(size),
            emmc?.Specification ?? _device?.Kind(_text),
            emmc?.TransferMode, emmc?.Cache, emmc?.Queue,
            nativeFirmware ? emmc?.Firmware : _device?.ProductRevision,
            serial, _device?.Vendor,
            _device?.Disk is { } disk ? $"PhysicalDrive{disk}" : null,
            DriveLetters()
        ];
        return DeviceFields.Select((field, index) =>
            (index == FirmwareRow && !nativeFirmware
                ? Pick("製品リビジョン", "Product revision")
                : Pick(field.Japanese, field.English), DisplayOr(values[index], "—"))).ToArray();
    }

    private string DriveLetters()
    {
        if (_device?.Volume is { } selectedVolume)
            return selectedVolume;
        if (_device?.Disk is not { } disk)
            return "—";
        string drives = string.Join(Pick("、", ", "), _inventory.Where(device => device.Disk == disk && device.Volume is not null)
            .Select(device => device.Volume).Distinct(StringComparer.Ordinal));
        return DisplayOr(drives, "—");
    }

    private static string DisplayOr(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private string FormatCapacity(ulong size) => size switch
    {
        0 => Pick("容量不明", "Unknown"),
        >= 1_000_000_000_000 => $"{size / 1_000_000_000_000.0:N2} TB",
        >= 1_000_000_000 => $"{size / 1_000_000_000.0:N1} GB",
        _ => $"{size / 1_000_000.0:N1} MB"
    };

    private unsafe void PopulateTable(bool languageChanged)
    {
        IReadOnlyList<RegisterRow> rows = _report.Rows;
        if (!languageChanged && _shownRows.SequenceEqual(rows))
            return;

        bool sameFields = _shownRows.Count == rows.Count && _shownRows.Zip(rows,
            (oldRow, newRow) => oldRow.Name == newRow.Name && oldRow.Address == newRow.Address).All(matching => matching);
        bool wasVisible = IsWindowVisible(_table) != 0;
        if (wasVisible)
            SendMessage(_table, WmSetRedraw, 0, 0);
        try
        {
            if (!sameFields)
                SendMessage(_table, LvDeleteAllItems, 0, 0);
            for (int index = 0; index < rows.Count; index++)
            {
                RegisterRow row = rows[index];
                if (sameFields && !languageChanged && row == _shownRows[index])
                    continue;
                if (!sameFields)
                {
                    SetCell(index, 0, row.Name);
                    SetCell(index, 1, row.Address);
                }
                SetCell(index, 2, row.Status is { } health ? _text.HealthName(health) : "—");
                SetCell(index, 3, row.Raw);
                SetCell(index, 4, row.Meaning);
            }
            _shownRows = rows;
        }
        finally
        {
            if (wasVisible)
                SendMessage(_table, WmSetRedraw, 1, 0);
            InvalidateRect(_table, null, 1);
        }
    }

    private unsafe void SetCell(int index, int column, string value)
    {
        fixed (char* text = value)
        {
            var item = new ListItem { Mask = 1, Item = index, SubItem = column, Text = text };
            SendMessage(_table, column == 0 ? LvInsertItem : LvSetItemText, column == 0 ? 0 : (nuint)index, (nint)(&item));
        }
    }

    private unsafe void WriteRegisterTip(ListInfoTip* tip)
    {
        if (tip->Item < 0 || tip->Item >= _report.Rows.Count || tip->Text == null || tip->Capacity <= 0)
            return;
        string text = RegisterDetails(_report.Rows[tip->Item]);
        int length = Math.Min(text.Length, tip->Capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(tip->Text, length));
        tip->Text[length] = '\0';
    }

    private void ShowSelectedRegister()
    {
        if (IsWindowEnabled(_window) == 0)
            return;
        if (SelectedRegister() is { } row)
            MessageBox(_window, RegisterDetails(row), Pick("レジスタの詳細", "Register Details"), 0);
    }

    private RegisterRow? SelectedRegister()
    {
        if (_rawVisible)
            return null;
        int index = (int)SendMessage(_table, LvGetNextItem, unchecked((nuint)(-1)), 2); // LVNI_SELECTED
        return index >= 0 && index < _report.Rows.Count ? _report.Rows[index] : null;
    }

    private static string RegisterDetails(RegisterRow row)
    {
        string raw = row.Raw.Length > 40
            ? string.Join("\r\n", Enumerable.Range(0, (row.Raw.Length + 39) / 40)
                .Select(i => row.Raw.Substring(i * 40, Math.Min(40, row.Raw.Length - i * 40))))
            : row.Raw;
        return $"{row.Name}  [{row.Address}]\r\n{row.Meaning}\r\n{raw}";
    }

    private void UpdateControls()
    {
        bool hasSelection = _targets.Count != 0;
        bool hasSd = _device?.Protocol == CardProtocol.Sd && IsCurrentDevice;
        EnableWindow(_combo, hasSelection ? 1 : 0);
        EnableMenuItem(_operationMenu, ReadId, hasSelection ? 0 : MfGrayed);
        EnableMenuItem(_operationMenu, SdId, hasSd ? 0 : MfGrayed);
        EnableMenuItem(_viewMenu, RawId, _report.RawText.Length > 0 ? 0 : MfGrayed);
        CheckMenuItem(_viewMenu, TableId, _rawVisible ? 0 : MfChecked);
        CheckMenuItem(_viewMenu, RawId, _rawVisible ? MfChecked : 0);
        CheckMenuItem(_viewMenu, HideSerialId, _preferences.HideSerial ? MfChecked : 0);
        EnableMenuItem(_fileMenu, SaveExtCsdId, ExportableExtCsd is not null ? 0 : MfGrayed);
        EnableMenuItem(_fileMenu, SaveReportId, CanExportReport ? 0 : MfGrayed);
        EnableMenuItem(_editMenu, CopyReportId, CanExportReport ? 0 : MfGrayed);
        EnableMenuItem(_editMenu, CopyRowId, IsCurrentDevice && SelectedRegister() is not null ? 0 : MfGrayed);
        int selected = (int)SendMessage(_combo, CbGetCurSel, 0, 0);
        for (int i = 0; i < _targets.Count; i++)
            CheckMenuItem(_deviceMenu, (uint)(FirstDeviceId + i), i == selected ? MfChecked : 0);
    }

    private void ShowDataView()
    {
        ShowWindow(_table, _rawVisible ? SwHide : SwShowNa);
        ShowWindow(_raw, _rawVisible ? SwShowNa : SwHide);
        bool empty = !_rawVisible && _report.Rows.Count == 0;
        ShowWindow(_empty, empty ? SwShowNa : SwHide);
        if (empty)
            SetWindowPos(_empty, 0, 0, 0, 0, 0, 0x13);
        SetWindowText(_section, _rawVisible ? Pick("レジスタの生データ", "Raw Register Data") : Pick("詳細データ", "Register Details"));
        UpdateControls();
    }

    private unsafe void RedrawHealth()
    {
        InvalidateRect(_window, null, 0);
        foreach (var labels in _lifeLabels)
            InvalidateRect(labels.Status, null, 1);
    }

    private static uint Color(uint rgb) => (rgb & 0xFF) << 16 | (rgb & 0xFF00) | (rgb >> 16);

    private uint Surface => _highContrast ? GetSysColor(5) : Paper;
    private uint Foreground => _highContrast ? GetSysColor(8) : Ink;
    private uint Rules => _highContrast ? GetSysColor(8) : Line;
    private uint AccentSurface => _highContrast ? Surface : Accent;

    private Tone HealthTone(Health health)
    {
        if (_highContrast)
            return new(Foreground, Surface);
        return health switch
        {
            Health.Healthy => new(Color(0x115E48), Color(0xDBEFE5)),
            Health.Normal => new(Color(0x144C79), Accent),
            Health.Caution => new(Color(0x794600), Color(0xFFF0CE)),
            Health.Warning => new(Color(0x883A14), Color(0xFFE5D5)),
            Health.Critical => new(Color(0x9A2036), Color(0xFAE2E7)),
            Health.Exceeded => new(Color(0x722667), Color(0xF2E2F0)),
            _ => new(Color(0x425D56), Color(0xE4ECE9))
        };
    }

    private nint Brush(uint color)
    {
        if (!_brushes.TryGetValue(color, out nint brush))
        {
            brush = CreateSolidBrush(color);
            _brushes.Add(color, brush);
        }
        return brush;
    }

    private unsafe void Fill(nint dc, Rect bounds, uint color) => FillRect(dc, &bounds, Brush(color));

    private Rect RectAt(int left, int top, int right, int bottom) =>
        new(Scale(left), Scale(top), Scale(right), Scale(bottom));

    private unsafe void PaintFrame(nint window)
    {
        Paint paint = default;
        nint dc = BeginPaint(window, &paint);
        try
        {
            if (dc != 0)
                DrawFrame(window, dc);
        }
        finally
        {
            EndPaint(window, &paint);
        }
    }

    private unsafe void DrawFrame(nint window, nint dc)
    {
        Rect bounds = default;
        GetClientRect(window, &bounds);
        Fill(dc, bounds, Surface);
        Fill(dc, new(0, 0, bounds.Right, Scale(StripHeight)), AccentSurface);
        Fill(dc, new(0, Scale(StripHeight - 1), bounds.Right, Scale(StripHeight)), Rules);

        int right = _width - Margin;
        for (int i = 0; i < _lifeHealth.Length; i++)
        {
            int top = LifeTop + LifePitch * i + 22;
            Rect badge = RectAt(LifeLeft, top, LifeLeft + LifeWidth, top + 33);
            Fill(dc, badge, AccentSurface);
            FrameRect(dc, &badge, Brush(Rules));
            Fill(dc, RectAt(LifeLeft, top, LifeLeft + 3, top + 33), HealthTone(_lifeHealth[i]).Ink);
        }
        int divider = MetadataLeft - 11;
        Fill(dc, RectAt(divider, 54, divider + 1, MetadataTop + DeviceFields.Length * MetadataPitch - 1), Rules);
        Fill(dc, RectAt(MetadataLeft, 90, right, 91), Rules);
        for (int i = 0; i < _metadataLabels.Length; i++)
        {
            int y = MetadataTop + i * MetadataPitch;
            Fill(dc, RectAt(MetadataValueLeft - 7, y, MetadataValueLeft - 6, y + MetadataHeight + 1), Rules);
            Fill(dc, RectAt(MetadataValueLeft - 7, y + MetadataHeight, right, y + MetadataHeight + 1), Rules);
        }
    }

    private nint PaintLabel(nint dc, LabelRole role, int index)
    {
        Tone tone = _highContrast ? new(Foreground, Surface) : role switch
        {
            LabelRole.StripLabel => new(Ink, Accent),
            LabelRole.StripSmall => new(Muted, Accent),
            LabelRole.LifeState => new(HealthTone(_lifeHealth[index]).Ink, Accent),
            LabelRole.LifeDetail => new(Muted, Accent),
            LabelRole.DeviceLabel or LabelRole.EmptyTable or LabelRole.Small => new(Muted, Paper),
            _ => new(Ink, Paper)
        };
        SetTextColor(dc, tone.Ink);
        SetBkColor(dc, tone.Surface);
        SetBkMode(dc, 2);
        return Brush(tone.Surface);
    }

    private unsafe nint PaintTable(ListDraw* draw)
    {
        if (draw->Stage is CddsPrePaint or CddsItemPrePaint)
            return CdrfNotifyItemDraw;
        if (draw->Stage != (CddsItemPrePaint | CddsSubItem) || _highContrast ||
            draw->ItemIndex >= (nuint)_report.Rows.Count ||
            SendMessage(_table, LvGetItemState, draw->ItemIndex, (nint)LvisSelected) != 0)
            return 0;

        var row = _report.Rows[(int)draw->ItemIndex];
        if (draw->SubItem == 2 && row.Status is { } status)
        {
            Tone cell = HealthTone(status);
            draw->TextColor = cell.Ink;
            draw->Background = cell.Surface;
        }
        else
        {
            draw->TextColor = draw->SubItem == 1 ? Muted : Ink;
            draw->Background = ((int)draw->ItemIndex & 1) == 0 ? Paper : Color(0xF6FAF8);
        }
        return CdrfNewFont;
    }
}
