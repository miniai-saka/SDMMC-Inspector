using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SdmmcInspector.NativeDialogTemplate;
using static SdmmcInspector.Win32;

namespace SdmmcInspector;

internal sealed partial class MainWindow
{
    private const int RefreshOffId = 120, RefreshCustomId = 121, RefreshPresetId = 130;
    private static readonly (string JapaneseMenu, string EnglishMenu, string JapaneseUnit, string EnglishUnit,
        int SecondsPerUnit, int[] Values)[] RefreshGroups =
    [
        ("秒単位(&S)", "&Seconds", "秒", "second", 1, [1, 2, 3, 5, 10, 15, 20, 30, 45]),
        ("分単位(&M)", "&Minutes", "分", "minute", 60, [1, 2, 3, 5, 10, 15, 20, 30, 45]),
        ("時間単位(&H)", "&Hours", "時間", "hour", 3600, [1, 2, 3, 4, 6, 8, 12, 24])
    ];

    private readonly List<(nint Menu, uint Id, int Seconds)> _refreshOptions = [];
    private nint _refreshMenu;
    private nuint _refreshTimer, _timerSequence;
    private bool _refreshProbeSd;

    private void AddRefreshMenus(nint settings)
    {
        _refreshOptions.Clear();
        _refreshMenu = AddPopup(settings, Pick("自動更新(&A)", "&Auto Refresh"),
            (RefreshOffId, Pick("無効(&O)", "&Off")),
            (RefreshCustomId, Pick("任意の間隔(&C)…", "&Custom Interval…")), (0, null));
        _refreshOptions.Add((_refreshMenu, RefreshOffId, 0));
        uint id = RefreshPresetId;
        foreach (var group in RefreshGroups)
        {
            var items = group.Values.Select((value, index) => (Id: id + (uint)index,
                Text: (string?)Pick($"{value} {group.JapaneseUnit}ごと",
                    $"Every {value} {group.EnglishUnit}{(value == 1 ? "" : "s")}"))).ToArray();
            nint menu = AddPopup(_refreshMenu, Pick(group.JapaneseMenu, group.EnglishMenu), items);
            for (int i = 0; i < items.Length; i++)
                _refreshOptions.Add((menu, items[i].Id, group.Values[i] * group.SecondsPerUnit));
            id += (uint)items.Length;
        }
        UpdateRefreshChecks();
    }

    private void UpdateRefreshChecks()
    {
        bool matched = false;
        foreach (var option in _refreshOptions)
        {
            bool selected = _preferences.RefreshSeconds == option.Seconds;
            CheckMenuItem(option.Menu, option.Id, selected ? MfChecked : 0);
            matched |= selected;
        }
        CheckMenuItem(_refreshMenu, RefreshCustomId, matched ? 0 : MfChecked);
    }

    private bool HandleRefreshCommand(int id)
    {
        if (id == RefreshCustomId)
        {
            if (RefreshDialog.Show(this) is { } seconds)
                ApplyRefreshSetting(seconds);
            return true;
        }
        foreach (var option in _refreshOptions)
        {
            if (option.Id != id)
                continue;
            ApplyRefreshSetting(option.Seconds);
            return true;
        }
        return false;
    }

    private nuint MakeRefreshTimer(int seconds) =>
        seconds == 0 ? 0 : SetTimer(_window, ++_timerSequence, (uint)seconds * 1000, 0);

    private void StopRefreshTimer()
    {
        if (_refreshTimer != 0)
            KillTimer(_window, _refreshTimer);
        _refreshTimer = 0;
    }

    private void RestoreRefreshTimer()
    {
        _refreshTimer = MakeRefreshTimer(_preferences.RefreshSeconds);
        if (_preferences.RefreshSeconds != 0 && _refreshTimer == 0)
        {
            _preferences = _preferences with { RefreshSeconds = 0 };
            UpdateRefreshChecks();
            MessageBox(_window, Pick("自動更新を開始できませんでした。", "Could not start auto refresh."), AppName, 0x10);
        }
    }

    private void ApplyRefreshSetting(int seconds)
    {
        if (_preferences.RefreshSeconds == seconds)
            return;
        nuint timer = MakeRefreshTimer(seconds);
        if (seconds != 0 && timer == 0)
        {
            MessageBox(_window, Pick("自動更新を開始できませんでした。", "Could not start auto refresh."), AppName, 0x10);
            return;
        }

        if (!TrySavePreferences(_preferences with { RefreshSeconds = seconds }))
        {
            if (timer != 0)
                KillTimer(_window, timer);
            return;
        }

        StopRefreshTimer();
        _refreshTimer = timer;
        UpdateRefreshChecks();
    }

    private sealed class RefreshDialog(MainWindow owner)
    {
        private const int Ok = 1, Cancel = 2, Hours = 301, Minutes = 302, Seconds = 303, Message = 304;
        private static RefreshDialog? _active;
        private int? _chosen;
        private string Title => owner.Pick("自動更新の間隔", "Auto Refresh Interval");

        internal static unsafe int? Show(MainWindow owner)
        {
            if (_active is not null)
                return null;
            var dialog = new RefreshDialog(owner);
            _active = dialog;
            try
            {
                byte[] template = dialog.BuildTemplate();
                fixed (byte* address = template)
                {
                    if (DialogBoxIndirectParam(owner._module, address, owner._window, &DialogProcedure, 0) == -1)
                        MessageBox(owner._window, owner.Pick("自動更新の設定画面を開けませんでした。",
                            "Could not open the auto refresh settings."), dialog.Title, 0x10);
                }
                return dialog._chosen;
            }
            finally
            {
                _active = null;
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static nint DialogProcedure(nint window, uint message, nuint wParam, nint lParam)
        {
            try
            {
                return _active!.HandleMessage(window, message, wParam);
            }
            catch (Exception error)
            {
                MessageBox(window, error.Message, _active!.Title, 0x10);
                EndDialog(window, Cancel);
                return 1;
            }
        }

        private nint HandleMessage(nint window, uint message, nuint wParam)
        {
            switch (message)
            {
                case WmInitDialog:
                    int seconds = owner._preferences.RefreshSeconds;
                    if (seconds == 0)
                        seconds = 60;
                    SetValue(window, Hours, seconds / 3600);
                    SetValue(window, Minutes, seconds / 60 % 60);
                    SetValue(window, Seconds, seconds % 60);
                    Validate(window);
                    return 1;
                case WmCtlColorDialog:
                    return owner.Brush(owner.Surface);
                case WmCtlColorStatic:
                    SetBkColor((nint)wParam, owner.Surface);
                    SetTextColor((nint)wParam, owner.Foreground);
                    return owner.Brush(owner.Surface);
                case WmCommand:
                    switch ((int)(wParam & 0xFFFF))
                    {
                        case Hours or Minutes or Seconds when (wParam >> 16 & 0xFFFF) == 0x0300: // EN_CHANGE
                            Validate(window);
                            return 1;
                        case Ok:
                            if (Validate(window) is { } valid)
                            {
                                _chosen = valid;
                                EndDialog(window, Ok);
                            }
                            return 1;
                        case Cancel:
                            EndDialog(window, Cancel);
                            return 1;
                    }
                    break;
                case WmClose:
                    EndDialog(window, Cancel);
                    return 1;
            }
            return 0;
        }

        private static void SetValue(nint window, int id, int value) =>
            SetWindowText(GetDlgItem(window, id), value.ToString("D2", CultureInfo.InvariantCulture));

        private int? Validate(nint window)
        {
            int hours = ReadTwoDigits(window, Hours);
            int minutes = ReadTwoDigits(window, Minutes);
            int seconds = ReadTwoDigits(window, Seconds);
            int total = 3600 * hours + 60 * minutes + seconds;
            bool valid = hours is >= 0 and <= 24 && minutes is >= 0 and < 60 && seconds is >= 0 and < 60 &&
                total is > 0 and <= AppPreferences.MaximumRefresh;
            EnableWindow(GetDlgItem(window, Ok), valid ? 1 : 0);
            SetWindowText(GetDlgItem(window, Message), valid
                ? owner.Pick($"更新間隔  {hours:D2}:{minutes:D2}:{seconds:D2}", $"Interval  {hours:D2}:{minutes:D2}:{seconds:D2}")
                : minutes is < 0 or > 59 || seconds is < 0 or > 59
                    ? owner.Pick("分と秒は 0～59 の整数で入力してください。", "Enter minutes and seconds from 0 to 59.")
                    : owner.Pick("1 秒から 24 時間までの間隔を入力してください。", "Enter an interval from 1 second to 24 hours."));
            return valid ? total : null;
        }

        private byte[] BuildTemplate() => Build(Title, 238, 98, owner.UiFont,
        [
            new(-1, StaticClass, StaticStyle, 12, 9, 214, 12,
                owner.Pick("選択中の機器を読み取る間隔を設定します。", "Set how often to read the selected device.")),
            new(-1, StaticClass, StaticStyle, 12, 29, 34, 13, owner.Pick("時間(&H)", "&Hours")),
            new(Hours, EditClass, EditStyle, 47, 27, 26, 15, ExtendedStyle: ClientEdge),
            new(-1, StaticClass, StaticStyle, 80, 29, 33, 13, owner.Pick("分(&M)", "&Minutes")),
            new(Minutes, EditClass, EditStyle, 113, 27, 26, 15, ExtendedStyle: ClientEdge),
            new(-1, StaticClass, StaticStyle, 148, 29, 37, 13, owner.Pick("秒(&S)", "&Seconds")),
            new(Seconds, EditClass, EditStyle, 185, 27, 26, 15, ExtendedStyle: ClientEdge),
            new(Message, StaticClass, StaticStyle, 12, 51, 214, 14),
            new(Ok, ButtonClass, ButtonStyle | 1, 111, 72, 50, 16, owner.Pick("保存", "Save")),
            new(Cancel, ButtonClass, ButtonStyle, 168, 72, 58, 16, owner.Pick("キャンセル", "Cancel"))
        ]);
    }
}
