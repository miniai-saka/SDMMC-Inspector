using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SdmmcInspector.NativeDialogTemplate;
using static SdmmcInspector.Win32;

namespace SdmmcInspector;

internal sealed partial class MainWindow
{
    private sealed class ThresholdDialog(MainWindow owner)
    {
        private const int Ok = 1, Cancel = 2, NormalInput = 201, CautionInput = 202, Preview = 203, Reset = 204;
        private static ThresholdDialog? _active;
        private readonly LifeThresholds _initial = owner._preferences.Thresholds;
        private LifeThresholds? _applied;
        private bool _valid;
        private string Title => owner.Pick("寿命判定のしきい値", "Lifetime Thresholds");

        internal static unsafe LifeThresholds? Show(MainWindow owner)
        {
            if (_active is not null)
                return null;
            var dialog = new ThresholdDialog(owner);
            _active = dialog;
            try
            {
                byte[] template = dialog.BuildTemplate();
                fixed (byte* address = template)
                {
                    if (DialogBoxIndirectParam(owner._module, address, owner._window, &DialogProcedure, 0) == -1)
                        throw new Win32Exception(Marshal.GetLastPInvokeError(),
                            owner.Pick("設定画面を開けませんでした。", "Could not open the lifetime threshold settings."));
                }
                return dialog._applied;
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
                return _active!.HandleMessage(window, message, wParam, lParam);
            }
            catch (Exception error)
            {
                MessageBox(window, error.Message, _active!.Title, 0x10);
                EndDialog(window, Cancel);
                return 1;
            }
        }

        private nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
        {
            switch (message)
            {
                case WmInitDialog:
                    SetLimits(window, _initial);
                    return 1;
                case WmCtlColorDialog:
                    return owner.Brush(owner.Surface);
                case WmCtlColorStatic:
                    return PaintStatic(window, (nint)wParam, lParam);
                case WmCommand:
                    switch ((int)(wParam & 0xFFFF))
                    {
                        case NormalInput or CautionInput when (wParam >> 16 & 0xFFFF) == 0x0300: // EN_CHANGE
                            RefreshPreview(window);
                            return 1;
                        case Reset:
                            SetLimits(window, LifeThresholds.Defaults);
                            return 1;
                        case Ok:
                            SaveLimits(window);
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

        private nint PaintStatic(nint window, nint dc, nint control)
        {
            uint color = owner.Foreground;
            uint fill = owner.Surface;
            if (control == GetDlgItem(window, Preview) && !owner._highContrast)
            {
                Tone alert = owner.HealthTone(Health.Critical);
                fill = _valid ? Accent : alert.Surface;
                color = _valid ? Ink : alert.Ink;
            }
            SetBkColor(dc, fill);
            SetTextColor(dc, color);
            SetBkMode(dc, 2);
            return owner.Brush(fill);
        }

        private void SetLimits(nint window, LifeThresholds limits)
        {
            SetWindowText(GetDlgItem(window, NormalInput), limits.NormalLimit.ToString(CultureInfo.InvariantCulture));
            SetWindowText(GetDlgItem(window, CautionInput), limits.CautionLimit.ToString(CultureInfo.InvariantCulture));
            RefreshPreview(window);
        }

        private LifeThresholds RefreshPreview(nint window)
        {
            var limits = new LifeThresholds(ReadTwoDigits(window, NormalInput), ReadTwoDigits(window, CautionInput));
            _valid = limits.IsValid;
            string preview = _valid
                ? owner.Pick($"正常 0–{limits.NormalLimit}%　　注意 {limits.NormalLimit}–{limits.CautionLimit}%　　警告 {limits.CautionLimit}–100%",
                    $"Normal 0–{limits.NormalLimit}%    Caution {limits.NormalLimit}–{limits.CautionLimit}%    Warning {limits.CautionLimit}–100%")
                : limits.NormalLimit is < 1 or > 99 || limits.CautionLimit is < 1 or > 99
                    ? owner.Pick("両方の上限を 1～99% の整数で入力してください。", "Enter both limits as whole numbers from 1 to 99%.")
                    : owner.Pick("正常の上限を注意の上限より小さくしてください。", "The Normal limit must be lower than the Caution limit.");
            SetWindowText(GetDlgItem(window, Preview), preview);
            EnableWindow(GetDlgItem(window, Ok), _valid ? 1 : 0);
            return limits;
        }

        private void SaveLimits(nint window)
        {
            LifeThresholds limits = RefreshPreview(window);
            if (!_valid)
                return;
            try
            {
                if (limits != _initial)
                    owner._preferenceStore.Save(owner._preferences with { Thresholds = limits });
                _applied = limits;
                EndDialog(window, Ok);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                MessageBox(window, owner.Pick("設定を保存できませんでした。設定フォルダーへのアクセスを確認してください。",
                    "Could not save settings. Check access to the settings folder."), Title, 0x10);
            }
        }

        private byte[] BuildTemplate()
        {
            Control[] controls =
            [
                new(-1, StaticClass, StaticStyle, 12, 9, 245, 12,
                    owner.Pick("eMMC の推定寿命 A・B に適用します。", "Applies to eMMC lifetime estimates A and B.")),
                new(-1, StaticClass, StaticStyle, 12, 28, 112, 13, owner.Pick("正常の上限(&N)", "&Normal limit")),
                new(NormalInput, EditClass, EditStyle, 125, 26, 42, 15, ExtendedStyle: ClientEdge),
                new(-1, StaticClass, StaticStyle, 173, 28, 20, 13, "%"),
                new(-1, StaticClass, StaticStyle, 12, 47, 112, 13, owner.Pick("注意の上限(&C)", "&Caution limit")),
                new(CautionInput, EditClass, EditStyle, 125, 45, 42, 15, ExtendedStyle: ClientEdge),
                new(-1, StaticClass, StaticStyle, 173, 47, 20, 13, "%"),
                new(Preview, StaticClass, StaticStyle | 0x0201, 12, 68, 246, 19),
                new(-1, StaticClass, StaticStyle, 12, 93, 246, 20,
                    owner.Pick("推定寿命は 10% 刻みです。各範囲の上限で判定します。", "The upper end of each 10% range sets the status.")),
                new(Reset, ButtonClass, ButtonStyle, 12, 115, 78, 16, owner.Pick("初期値に戻す(&D)", "Reset &Defaults")),
                new(Ok, ButtonClass, ButtonStyle | 1, 145, 115, 50, 16, owner.Pick("保存", "Save")),
                new(Cancel, ButtonClass, ButtonStyle, 202, 115, 56, 16, owner.Pick("キャンセル", "Cancel"))
            ];

            return Build(Title, 270, 141, owner.UiFont, controls);
        }
    }
}
