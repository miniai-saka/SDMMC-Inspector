using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SdmmcInspector.Win32;

namespace SdmmcInspector;

internal sealed partial class MainWindow
{
    private const string AppName = "SDMMC Inspector";
    private const string ClassName = "SdmmcInspector.MainWindow";
    private const uint FrameStyle = WsOverlappedWindow | WsClipChildren;
    private const int MinimumWidth = 608, MinimumHeight = 474;
    private const int ComboId = 101, ScanId = 102, ReadId = 103, SdId = 104, RawId = 105;
    private const int ExitId = 106, SettingsId = 107, TableId = 108;

    private static MainWindow? _instance;

    private readonly ConcurrentQueue<Action> _updates = new();
    private PendingOperation? _operation;
    private List<Device> _inventory = [];
    private List<Device> _targets = [];
    private Device? _device;
    private Func<UiText, HealthReport> _reportSource = SearchingReport;
    private HealthReport _report = SearchingReport(default);
    private AppPreferences _preferences;
    private UiText _text;
    private DateTime? _updated;
    private nint _window, _module;
    private int _dpi = 96;
    private bool _selectionPending, _ready, _rawVisible, _reportedFatalError;

    internal static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == CoreClient.ReaderMode)
            return RunNativeReader();

        var application = new MainWindow();
        _instance = application;
        try
        {
            return application.Run();
        }
        catch (Exception exception)
        {
            MessageBox(0, exception.Message, AppName, 0x10);
            return 1;
        }
    }

    private unsafe int Run()
    {
        _preferences = AppPreferences.Load();
        _text = UiText.Resolve(_preferences.Language, GetUserDefaultUILanguage());
        _report = _reportSource(_text);
        _module = GetModuleHandle(0);
        CommonControls controls = new() { Size = (uint)sizeof(CommonControls), Classes = 1 };
        if (InitCommonControlsEx(&controls) == 0)
            throw new InvalidOperationException(Pick("Windows の共通コントロールを初期化できませんでした。",
                "Could not initialize Windows common controls."));

        nint icon = LoadIcon(_module, 32512);
        if (icon == 0)
            icon = LoadIcon(0, 32512);
        nint smallIcon = LoadImage(_module, 32512, 1, GetSystemMetrics(49), GetSystemMetrics(50), 0x8000); // LR_SHARED

        fixed (char* className = ClassName)
        {
            WindowClass settings = new()
            {
                Size = (uint)sizeof(WindowClass), Procedure = &WindowProcedure, ClassName = className,
                Instance = _module, Cursor = LoadCursor(0, 32512), Icon = icon, SmallIcon = smallIcon != 0 ? smallIcon : icon
            };
            if (RegisterClassEx(&settings) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(),
                    Pick("ウィンドウクラスを登録できませんでした。", "Could not register the window class."));
        }

        _dpi = (int)GetDpiForSystem();
        if (_dpi == 0)
            _dpi = 96;
        nint menu = CreateMenus();
        Rect size = new(0, 0, Scale(664), Scale(648));
        AdjustWindowRectExForDpi(&size, FrameStyle, 1, 0, (uint)_dpi);
        if (CreateWindowEx(0, ClassName, AppName, FrameStyle, CwUseDefault, CwUseDefault,
            size.Width, size.Height, 0, menu, _module, 0) == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            DestroyMenu(menu);
            if (_reportedFatalError)
                return 1;
            throw new Win32Exception(error, Pick("ウィンドウを開けませんでした。", "Could not open the window."));
        }

        ShowWindow(_window, SwShow);
        RestoreRefreshTimer();
        StartScan();
        for (;;)
        {
            Message message = default;
            int result = GetMessage(&message, 0, 0, 0);
            if (result <= 0)
                return result == 0 ? (int)message.WParam : 1;
            if (HandleShortcut(message) || IsDialogMessage(_window, &message) != 0)
                continue;
            TranslateMessage(&message);
            DispatchMessage(&message);
        }
    }

    private string Pick(string japanese, string english) => _text.Pick(japanese, english);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        MainWindow app = _instance!;
        try
        {
            return app.HandleMessage(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            if (!app._reportedFatalError)
            {
                app._reportedFatalError = true;
                MessageBox(window, exception.Message, AppName, 0x10);
            }
            if (message == WmCreate)
                return -1;
            PostQuitMessage(1);
            return 0;
        }
    }

    private unsafe nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WmCreate:
                _window = window;
                uint windowDpi = GetDpiForWindow(window);
                if (windowDpi != 0)
                    _dpi = (int)windowDpi;
                CreateControls();
                PopulateCombo(0);
                _ready = true;
                Layout();
                Render();
                return 0;
            case WmSize when _ready && wParam != 1:
                Layout();
                return 0;
            case WmGetMinMaxInfo:
                Rect size = new(0, 0, Scale(MinimumWidth), Scale(MinimumHeight));
                AdjustWindowRectExForDpi(&size, FrameStyle, 1, 0, (uint)_dpi);
                ((MinMaxInfo*)lParam)->MinTrackSize = new Point { X = size.Width, Y = size.Height };
                return 0;
            case WmDpiChanged when _ready:
                _dpi = (int)(wParam & 0xFFFF);
                ApplyFonts();
                Rect destination = *(Rect*)lParam;
                SetWindowPos(window, 0, destination.Left, destination.Top, destination.Width, destination.Height, 0x14);
                Layout();
                return 0;
            case WmPaint:
                PaintFrame(window);
                return 0;
            case WmPrintClient when _ready:
                DrawFrame(window, (nint)wParam);
                return 0;
            case WmEraseBackground:
                return 1;
            case WmSettingChange or WmSysColorChange when _ready:
                ApplySystemColors();
                if (message == WmSettingChange && _preferences.Language == AppLanguage.System && IsWindowEnabled(window) != 0)
                    ApplyLanguage();
                break;
            case WmInitMenuPopup when _ready:
                UpdateControls();
                break;
            case WmCtlColorStatic:
                if (_labelRoles.TryGetValue(lParam, out var label))
                    return PaintLabel((nint)wParam, label.Role, label.Index);
                break;
            case WmNotify when _ready:
                var header = (NotifyHeader*)lParam;
                if (header->Source == _table)
                {
                    if (header->Code == NmCustomDraw)
                        return PaintTable((ListDraw*)lParam);
                    if (header->Code == LvnGetInfoTip)
                        WriteRegisterTip((ListInfoTip*)lParam);
                    if (header->Code is NmDoubleClick or NmReturn)
                        ShowSelectedRegister();
                }
                break;
            case WmTimer when wParam == _refreshTimer && _refreshTimer != 0:
                if (_operation is null && !_selectionPending && IsWindowEnabled(window) != 0 &&
                    _device is { Protocol: CardProtocol.Mmc or CardProtocol.Sd } current &&
                    SendMessage(_combo, CbGetDroppedState, 0, 0) == 0 && Selection()?.Target == current.Target)
                    StartRead(probe: _refreshProbeSd && current.Protocol == CardProtocol.Sd, automatic: true);
                return 0;
            case WmCommand when _ready:
                if (HandleCommand((int)(wParam & 0xFFFF), (int)(wParam >> 16 & 0xFFFF)))
                    return 0;
                break;
            case WmAppResult:
                while (_updates.TryDequeue(out Action? update))
                    update();
                return 0;
            case WmAppSelection:
                _selectionPending = false;
                if (SendMessage(_combo, CbGetDroppedState, 0, 0) == 0 && Selection()?.Target != _device?.Target)
                    StartRead(probe: false);
                return 0;
            case WmClose:
                DestroyWindow(window);
                return 0;
            case WmDestroy:
                StopRefreshTimer();
                _operation?.Cancel();
                _operation = null;
                FreeDrawingResources();
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private bool HandleCommand(int id, int code)
    {
        if (id == ComboId)
        {
            // Let the combo box finish committing its selection before starting a read.
            if (!_selectionPending && (code is CbnCloseup or CbnSelChange))
                _selectionPending = PostMessage(_window, WmAppSelection, 0, 0) != 0;
            return true;
        }
        if (code != 0)
            return false;
        if (id >= FirstDeviceId && id < FirstDeviceId + _targets.Count)
        {
            SendMessage(_combo, CbSetCurSel, (nuint)(id - FirstDeviceId), 0);
            if (Selection()?.Target != _device?.Target)
                StartRead(probe: false);
            return true;
        }
        switch (id)
        {
            case ScanId: StartScan(); return true;
            case ReadId: StartRead(probe: false); return true;
            case SdId: StartRead(probe: true); return true;
            case RawId: ChangeDataView(!_rawVisible); return true;
            case TableId: ChangeDataView(raw: false); return true;
            case SettingsId: EditThresholds(); return true;
            case SystemLanguageId: ChangeLanguage(AppLanguage.System); return true;
            case JapaneseLanguageId: ChangeLanguage(AppLanguage.Japanese); return true;
            case EnglishLanguageId: ChangeLanguage(AppLanguage.English); return true;
            case HideSerialId: ToggleSerial(); return true;
            case SaveExtCsdId: ExportExtCsd(); return true;
            case SaveReportId: ExportReport(); return true;
            case SaveImageId: ExportImage(); return true;
            case CopyReportId: CopyReport(); return true;
            case CopyRowId: CopySelectedRegister(); return true;
            case DiskManagementId: OpenWindowsTool("diskmgmt.msc"); return true;
            case DeviceManagerId: OpenWindowsTool("devmgmt.msc"); return true;
            case HelpId: ShowHelp(); return true;
            case AboutId: ShowAbout(); return true;
            case ExitId: PostMessage(_window, WmClose, 0, 0); return true;
            default: return HandleRefreshCommand(id);
        }
    }

    private void ChangeDataView(bool raw)
    {
        if (raw && _report.RawText.Length == 0 || _rawVisible == raw)
            return;
        _rawVisible = raw;
        ShowDataView();
        SetFocus(raw ? _raw : _table);
    }

    private void EditThresholds()
    {
        try
        {
            if (ThresholdDialog.Show(this) is not { } limits || limits == _preferences.Thresholds)
                return;
            _preferences = _preferences with { Thresholds = limits };
            RenderReport();
        }
        catch (Win32Exception error)
        {
            MessageBox(_window, error.Message, AppName, 0x10);
        }
    }

    private Device? Selection()
    {
        int index = (int)SendMessage(_combo, CbGetCurSel, 0, 0);
        return index >= 0 && index < _targets.Count ? _targets[index] : null;
    }

    private static HealthReport SearchingReport(UiText text) => HealthReport.Unavailable(
        text.Pick("機器を検索しています", "Finding devices"),
        text.Pick("接続機器を検索しています。", "Searching for connected devices…"));

    private void ShowReport(Func<UiText, HealthReport> source)
    {
        _reportSource = source;
        RenderReport();
    }

    private void RenderReport(bool languageChanged = false)
    {
        _report = _reportSource(_text);
        Render(languageChanged);
    }

    private void StartScan()
    {
        string? previous = Selection()?.Target;
        _device = null;
        _refreshProbeSd = false;
        _updated = null;
        _rawVisible = false;
        ShowReport(SearchingReport);
        StartOperation(CoreClient.ListAsync, inventory => ShowInventory(inventory, previous), error =>
        {
            _inventory = [];
            _targets = [];
            PopulateCombo(0);
            ShowError(error, "機器を検出できません", "Device detection failed");
        });
    }

    private void ShowInventory(List<Device> inventory, string? previous)
    {
        _inventory = inventory;
        var physical = inventory.Where(device => device.Volume is null).ToDictionary(device => device.Disk!.Value);
        _targets = inventory
            .Where(device => device.Volume is null || device.HasCardBus || device.Disk is null ||
                !physical.TryGetValue(device.Disk.Value, out Device? disk) || disk.HasCardBus)
            .OrderBy(device => device.Protocol != CardProtocol.Unknown ? 0 : device.HasCardBus ? 1
                : device.BusType == 7 ? 2 : 3)
            .ThenBy(device => device.Disk)
            .ThenBy(device => device.Volume, StringComparer.Ordinal)
            .ToList();
        int selection = _targets.FindIndex(device => device.Target == previous);
        PopulateCombo(Math.Max(selection, 0));
        if (_targets.Count == 0)
        {
            ShowReport(text => HealthReport.Unavailable(text.Pick("接続機器が見つかりません", "No connected devices"),
                text.Pick("機器を接続して「再検出」を選択してください。", "Connect a device and choose Rescan Devices.")));
            return;
        }
        StartRead(probe: false);
    }

    private void StartRead(bool probe, bool automatic = false)
    {
        if (Selection() is not { } selected)
            return;
        bool sameTarget = _device?.Target == selected.Target;
        CardProtocol protocol = sameTarget ? _device!.Protocol : selected.Protocol;
        if (probe && protocol != CardProtocol.Sd)
            return;
        if (!automatic)
            _refreshProbeSd = probe;
        if (!sameTarget)
        {
            _device = selected;
            _updated = null;
            _rawVisible = false;
            ShowReport(text => HealthReport.Unavailable(text.Pick("読み取っています", "Reading device"),
                text.Pick($"{selected.Target} の情報を読み取っています。", $"Reading {selected.Target}…")));
        }
        StartOperation(token => CoreClient.ReadAsync(selected, probe, token), read =>
        {
            _device = read.Device ?? selected;
            _updated = DateTime.Now;
            ShowReport(text => HealthReport.From(read, selected, probe, _preferences.Thresholds, text));
        }, error =>
        {
            _updated = DateTime.Now;
            ShowError(error, "読み取りに失敗しました", "Device read failed");
        });
    }

    private void StartOperation<T>(Func<CancellationToken, Task<T>> work, Action<T> success, Action<Exception> failure)
    {
        _operation?.Cancel();
        var operation = new PendingOperation();
        _operation = operation;
        CancellationToken cancellation = operation.Token;
        _ = Task.Run(async () =>
        {
            Action completion;
            try
            {
                T data = await work(cancellation).ConfigureAwait(false);
                completion = () => success(data);
            }
            catch (Exception exception)
            {
                completion = () => failure(exception);
            }
            _updates.Enqueue(() =>
            {
                bool current = ReferenceEquals(_operation, operation);
                if (current)
                    _operation = null;
                _ = operation.ReleaseAsync();
                if (current && !cancellation.IsCancellationRequested)
                    completion();
            });
            PostMessage(_window, WmAppResult, 0, 0);
        });
    }

    private sealed class PendingOperation
    {
        private readonly CancellationTokenSource _source = new();
        private Task _cancellation = Task.CompletedTask;

        internal CancellationToken Token => _source.Token;

        internal void Cancel() => _cancellation = _source.CancelAsync();

        internal async Task ReleaseAsync()
        {
            try { await _cancellation.ConfigureAwait(false); }
            finally { _source.Dispose(); }
        }
    }

    private void ShowError(Exception error, string japanese, string english)
    {
        ShowReport(text => HealthReport.Unavailable(text.Pick(japanese, english),
            error is CoreError core ? core.Describe(text) : text.Pick(
                "読み取り処理の応答を解釈できませんでした。",
                "Could not interpret the reader response.")));
    }
}
