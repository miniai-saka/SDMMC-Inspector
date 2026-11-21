using System.ComponentModel;
using System.Globalization;
using System.Text;
using static SdmmcInspector.Win32;

namespace SdmmcInspector;

internal sealed partial class MainWindow
{
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private bool IsCurrentDevice => _device is not null && Selection()?.Target == _device.Target;
    private byte[]? ExportableExtCsd => IsCurrentDevice ? _report.ExtCsdBytes : null;
    private bool CanExportReport => IsCurrentDevice && _updated.HasValue;

    private void ExportExtCsd()
    {
        if (ExportableExtCsd is not { Length: 512 } bytes)
            return;
        SaveExport("bin", "EXT_CSD.bin", Pick("EXT_CSD を書き出す（512 バイト）", "Export EXT_CSD (512 bytes)"),
            Pick("EXT_CSD バイナリ", "EXT_CSD binary"), path => File.WriteAllBytes(path, bytes));
    }

    private void ExportReport()
    {
        if (!CanExportReport)
            return;
        string report = BuildTextReport();
        SaveExport("txt", "SDMMC-Inspector.txt", Pick("レポートを保存", "Save Report"),
            Pick("テキスト レポート", "Text report"), path => File.WriteAllText(path, report, Utf8WithBom));
    }

    private void ExportImage()
    {
        try
        {
            using WindowSnapshot snapshot = WindowSnapshot.Capture(_window);
            SaveExport("png", "SDMMC-Inspector.png", Pick("画面を保存", "Save Image"),
                Pick("PNG 画像", "PNG image"), snapshot.SavePng);
        }
        catch (IOException)
        {
            MessageBox(_window, Pick("画面を画像として取得できませんでした。", "Could not capture the application window."), AppName, 0x10);
        }
    }

    private void SaveExport(string extension, string fileName, string title, string type, Action<string> write)
    {
        try
        {
            if (NativeExport.ChoosePath(_window, title, fileName, type, extension) is { } path)
                NativeExport.Save(path, write);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        {
            MessageBox(_window, Pick("ファイルを保存できませんでした。保存先へのアクセスを確認してください。",
                "Could not save the file. Check access to the destination."), title, 0x10);
        }
    }

    private void CopyReport()
    {
        if (CanExportReport)
            CopyText(BuildTextReport());
    }

    private void CopySelectedRegister()
    {
        if (IsCurrentDevice && SelectedRegister() is { } row)
            CopyText(RegisterDetails(row));
    }

    private void CopyText(string content)
    {
        try { NativeExport.CopyText(_window, content); }
        catch (Win32Exception)
        {
            MessageBox(_window, Pick("クリップボードにコピーできませんでした。", "Could not copy to the clipboard."), AppName, 0x10);
        }
    }

    private string BuildTextReport()
    {
        var output = new StringBuilder();
        output.AppendLine(AppName).AppendLine(DisplayOr(_device?.Product, _device!.Target));
        output.Append(Pick("更新", "Updated")).Append('\t')
            .AppendLine(_updated!.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        output.AppendLine();
        foreach (var (label, value) in DeviceMetadata())
            output.Append(label).Append('\t').AppendLine(value);

        output.AppendLine().AppendLine(Pick("寿命ステータス", "Lifetime Status"));
        for (int i = 0; i < _lifeLabels.Length; i++)
            output.Append(_text.LifeName(i)).Append('\t').Append(_text.HealthName(_lifeHealth[i])).Append('\t').AppendLine(LifeDetail(i));
        if (_report.Detail.Length != 0)
            output.AppendLine().AppendLine(_report.Detail);
        if (_report.Notice is { } notice)
            output.AppendLine(notice);

        if (_report.Rows.Count != 0)
        {
            output.AppendLine().AppendLine(Pick("詳細データ", "Register Details"));
            output.AppendLine(string.Join('\t', RegisterColumns.Select(column => Pick(column.Japanese, column.English))));
            foreach (RegisterRow row in _report.Rows)
            {
                output.Append(row.Name).Append('\t').Append(row.Address).Append('\t')
                    .Append(row.Status is { } state ? _text.HealthName(state) : "—").Append('\t')
                    .Append(row.Raw).Append('\t').AppendLine(row.Meaning);
            }
        }
        return output.ToString();
    }
}
