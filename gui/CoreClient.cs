using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SdmmcInspector;

internal enum CardProtocol { Unknown, Mmc, Sd }

internal sealed record Device(
    uint? Disk, string? Volume, uint BusType, string Bus, CardProtocol Protocol, uint ProtocolError,
    ulong SizeBytes, string Vendor, string Product, string ProductRevision, string Serial)
{
    internal string Target => Volume ?? $"PhysicalDrive{Disk}";
    internal string Kind(UiText text) => Protocol switch
    {
        CardProtocol.Mmc => "eMMC / MMC",
        CardProtocol.Sd => "SD",
        _ when BusType == 13 => "MMC",
        _ when BusType == 12 => "SD",
        _ when BusType == 0 => text.Pick("不明", "Unknown"),
        _ => Bus
    };
    internal bool HasCardBus => Protocol != CardProtocol.Unknown || BusType is 12 or 13;

    internal string MenuLabel(UiText text) => Product.Length == 0
        ? $"{Target}    [{Kind(text)}]"
        : $"{Target}    [{Kind(text)}]    {Product}";
}

internal sealed record RegisterRead(byte[]? Bytes, string? Issue = null, uint Code = 0, uint Received = 0);

internal sealed record Inspection(Device? Device, uint OpenError, uint SdOpenError, bool SdProtocolChanged,
    RegisterRead? ExtCsd, RegisterRead? Scr, RegisterRead? SdStatus, RegisterRead? Cmd6);

internal sealed class CoreError(string japanese, string english, Exception? inner = null) : IOException(english, inner)
{
    internal string Describe(UiText text) => text.Pick(japanese, Message);
}

internal static class CoreClient
{
    internal const string ReaderMode = "--sdmmc-reader";

    internal static async Task<List<Device>> ListAsync(CancellationToken cancel)
    {
        using JsonDocument document = await InvokeAsync(["--list"], cancel).ConfigureAwait(false);
        return document.RootElement.GetProperty("devices").EnumerateArray().Select(ParseDevice).ToList();
    }

    internal static async Task<Inspection> ReadAsync(Device device, bool probe, CancellationToken cancel)
    {
        List<string> args = device.Volume is { } letter
            ? ["--volume", letter]
            : ["--disk", device.Disk!.Value.ToString(CultureInfo.InvariantCulture)];
        if (probe)
            args.Add("--sd-probe");

        using JsonDocument document = await InvokeAsync(args, cancel).ConfigureAwait(false);
        JsonElement report = document.RootElement;
        if (report.TryGetProperty("openError", out JsonElement openError))
            return new(null, openError.GetUInt32(), 0, false, null, null, null, null);

        JsonElement registers = report.GetProperty("registers");
        return new(ParseDevice(report.GetProperty("device")), 0, GetCode(report, "sdOpenError"),
            report.TryGetProperty("sdProtocolChanged", out JsonElement changed) && changed.GetBoolean(),
            ParseRegister(registers, "extCsd", 512), ParseRegister(registers, "scr", 8),
            ParseRegister(registers, "sdStatus", 64), ParseRegister(registers, "cmd6", 64));
    }

    private static Device ParseDevice(JsonElement value)
    {
        JsonElement disk = value.GetProperty("disk");
        CardProtocol protocol = value.GetProperty("protocol").GetString() switch
        {
            "mmc" => CardProtocol.Mmc,
            "sd" => CardProtocol.Sd,
            _ => CardProtocol.Unknown
        };
        string? volume = value.GetProperty("volume").GetString();
        if (disk.ValueKind == JsonValueKind.Null && volume is null)
            throw new CoreError("機器番号とドライブ文字の両方が取得できませんでした。",
                "The device returned neither a disk number nor a drive letter.");

        return new(disk.ValueKind == JsonValueKind.Null ? null : disk.GetUInt32(), volume,
            value.GetProperty("busType").GetUInt32(), GetText(value, "bus"), protocol,
            GetCode(value, "protocolError"), value.GetProperty("sizeBytes").GetUInt64(),
            GetText(value, "vendor"), GetText(value, "product"), GetText(value, "revision"), GetText(value, "serial"));
    }

    private static string GetText(JsonElement value, string key) =>
        value.GetProperty(key).GetString()?.Trim() ?? "";

    private static uint GetCode(JsonElement value, string key) =>
        value.TryGetProperty(key, out JsonElement property) ? property.GetUInt32() : 0;

    private static RegisterRead? ParseRegister(JsonElement registers, string key, int size)
    {
        if (!registers.TryGetProperty(key, out JsonElement register))
            return null;
        if (!register.TryGetProperty("hex", out JsonElement hex))
            return new(null, GetText(register, "issue"), GetCode(register, "code"), GetCode(register, "received"));

        string value = hex.GetString() ?? "";
        if (value.Length == size * 2)
        {
            byte[] bytes = new byte[size];
            if (Convert.FromHexString(value, bytes, out _, out _) == System.Buffers.OperationStatus.Done)
                return new(bytes);
        }
        return new(null, "invalid-data");
    }

    private static async Task<JsonDocument> InvokeAsync(IReadOnlyList<string> arguments, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        string path = Environment.ProcessPath ?? throw new CoreError(
            "アプリの実行ファイルの場所を取得できませんでした。", "Could not locate the application's executable.");

        // Use the same executable in reader mode so a blocked device read can still be cancelled.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(TimeSpan.FromSeconds(25));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };
        process.StartInfo.ArgumentList.Add(ReaderMode);
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.ArgumentList.Add("--json");
        try
        {
            if (!process.Start())
                throw new CoreError("読み取り処理を起動できませんでした。", "Could not start the reader.");
        }
        catch (Win32Exception exception)
        {
            throw new CoreError($"読み取り処理を起動できませんでした (Win32 {exception.NativeErrorCode})。",
                $"Could not start the reader (Win32 {exception.NativeErrorCode}).", exception);
        }

        try
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
            Task stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, limit.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(limit.Token))
                .WaitAsync(limit.Token).ConfigureAwait(false);
            if (process.ExitCode is not (0 or 3))
                throw new CoreError($"読み取りツールが終了コード {process.ExitCode} を返しました。",
                    $"The reader exited with code {process.ExitCode}.");
            return JsonDocument.Parse(await stdout.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            cancel.ThrowIfCancellationRequested();
            throw new CoreError("25 秒以内に読み取りが完了しませんでした。接続を確認してください。",
                "Reading timed out after 25 seconds. Check the device connection.");
        }
        catch (JsonException exception)
        {
            throw new CoreError("読み取り処理の応答を解釈できませんでした。",
                "Could not interpret the reader response.", exception);
        }
    }
}
