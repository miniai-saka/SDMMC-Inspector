using System.ComponentModel;
using System.Runtime.InteropServices;
using static SdmmcInspector.Win32;

namespace SdmmcInspector;

internal static unsafe class NativeExport
{
    internal static string? ChoosePath(nint owner, string title, string fileName, string type, string extension)
    {
        const uint overwritePrompt = 2, noChangeDir = 8, pathMustExist = 0x800, explorer = 0x80000;
        char[] file = new char[32768];
        fileName.AsSpan().CopyTo(file);
        string filter = $"{type} (*.{extension})\0*.{extension}\0\0";
        fixed (char* fileBuffer = file, titleBuffer = title, filterBuffer = filter, extensionBuffer = extension)
        {
            OpenFileName dialog = new()
            {
                Size = (uint)sizeof(OpenFileName), Owner = owner, Title = titleBuffer, Filter = filterBuffer,
                FilterIndex = 1, File = fileBuffer, MaxFile = (uint)file.Length, DefaultExtension = extensionBuffer,
                Flags = overwritePrompt | noChangeDir | pathMustExist | explorer
            };
            if (GetSaveFileName(&dialog) != 0)
                return new string(fileBuffer);
        }
        uint error = CommDlgExtendedError();
        if (error != 0)
            throw new Win32Exception((int)error);
        return null;
    }

    internal static void Save(string path, Action<string> write)
    {
        string directory = Path.GetDirectoryName(path)!;
        string temporary = Path.Combine(directory, $".sdmmc-{Guid.NewGuid():N}.tmp");
        bool replacing = File.Exists(path);
        try
        {
            write(temporary);
            File.Move(temporary, path, overwrite: replacing);
        }
        catch
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    internal static void CopyText(nint owner, string text)
    {
        const uint moveable = 2, unicodeText = 13;
        nint memory = GlobalAlloc(moveable, checked((nuint)((text.Length + 1) * sizeof(char))));
        if (memory == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            char* address = (char*)GlobalLock(memory);
            if (address == null)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                text.AsSpan().CopyTo(new Span<char>(address, text.Length));
                address[text.Length] = '\0';
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (OpenClipboard(owner) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                if (EmptyClipboard() == 0 || SetClipboardData(unicodeText, memory) == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                memory = 0; // Windows owns the memory after SetClipboardData succeeds.
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            if (memory != 0)
                GlobalFree(memory);
        }
    }
}

internal sealed unsafe class WindowSnapshot : IDisposable
{
    private static readonly Guid PngEncoder = new("557cf406-1a04-11d3-9a73-0000f81ef32e");

    private nint _bitmap;
    private readonly nint _pixels;
    private readonly int _width, _height, _stride;

    private WindowSnapshot(nint bitmap, nint pixels, int width, int height, int stride) =>
        (_bitmap, _pixels, _width, _height, _stride) = (bitmap, pixels, width, height, stride);

    internal static WindowSnapshot Capture(nint window)
    {
        Rect area = default;
        if (GetWindowRect(window, &area) == 0 || area.Width <= 0 || area.Height <= 0)
            throw new IOException("Could not get the window size.");

        nint dc = CreateCompatibleDC(0);
        if (dc == 0)
            throw new IOException("Could not create a capture device context.");
        nint bitmap = 0;
        try
        {
            BitmapInfo info = new()
            {
                Size = (uint)sizeof(BitmapInfo), Width = area.Width, Height = -area.Height,
                Planes = 1, BitCount = 32
            };
            bitmap = CreateDIBSection(dc, &info, 0, out nint pixels, 0, 0);
            if (bitmap == 0 || pixels == 0)
                throw new IOException("Could not allocate the image bitmap.");
            nint original = SelectObject(dc, bitmap);
            if (original is 0 or -1)
                throw new IOException("Could not select the capture bitmap.");
            try
            {
                const uint invalidateFrameAndChildren = 0x0001 | 0x0080 | 0x0100 | 0x0400;
                RedrawWindow(window, null, 0, invalidateFrameAndChildren);
                const uint renderFullContent = 2;
                if (PrintWindow(window, dc, renderFullContent) == 0)
                    throw new IOException("Windows could not render the application window.");
                GdiFlush();
            }
            finally
            {
                SelectObject(dc, original);
            }
            Rect visible = default;
            const uint extendedFrameBounds = 9;
            if (DwmGetWindowAttribute(window, extendedFrameBounds, &visible, (uint)sizeof(Rect)) != 0 ||
                visible.Left < area.Left || visible.Top < area.Top || visible.Right > area.Right ||
                visible.Bottom > area.Bottom || visible.Width <= 0 || visible.Height <= 0)
                visible = area;
            int stride = area.Width * 4;
            nint visiblePixels = pixels + (visible.Top - area.Top) * stride + (visible.Left - area.Left) * 4;
            var snapshot = new WindowSnapshot(bitmap, visiblePixels, visible.Width, visible.Height, stride);
            bitmap = 0;
            return snapshot;
        }
        finally
        {
            if (bitmap != 0)
                DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    internal void SavePng(string path)
    {
        var startup = new GdiPlusStartupInput { Version = 1, SuppressExternalCodecs = 1 };
        Check(GdiplusStartup(out nuint token, &startup, 0));
        try
        {
            const int pixelFormat32bppRgb = 0x00022009;
            Check(GdipCreateBitmapFromScan0(_width, _height, _stride, pixelFormat32bppRgb, _pixels, out nint image));
            try
            {
                Guid encoder = PngEncoder;
                Check(GdipSaveImageToFile(image, path, &encoder, 0));
            }
            finally
            {
                GdipDisposeImage(image);
            }
        }
        finally
        {
            GdiplusShutdown(token);
        }
    }

    private static void Check(int status)
    {
        if (status != 0)
            throw new IOException($"The PNG encoder returned GDI+ status {status}.");
    }

    public void Dispose()
    {
        if (_bitmap != 0)
            DeleteObject(_bitmap);
        _bitmap = 0;
    }
}

internal static unsafe partial class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct OpenFileName
    {
        internal uint Size;
        internal nint Owner, Instance;
        internal char* Filter, CustomFilter;
        internal uint MaxCustomFilter, FilterIndex;
        internal char* File;
        internal uint MaxFile;
        internal char* FileTitle;
        internal uint MaxFileTitle;
        internal char* InitialDirectory, Title;
        internal uint Flags;
        internal ushort FileOffset, ExtensionOffset;
        internal char* DefaultExtension;
        internal nint CustomData, Hook, TemplateName, Reserved;
        internal uint ReservedFlags, ExtendedFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, BitCount;
        internal uint Compression, ImageSize;
        internal int HorizontalResolution, VerticalResolution;
        internal uint ColorsUsed, ImportantColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GdiPlusStartupInput
    {
        internal uint Version;
        internal nint DebugCallback;
        internal int SuppressBackgroundThread, SuppressExternalCodecs;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW")]
    internal static partial int GetSaveFileName(OpenFileName* dialog);

    [LibraryImport("comdlg32.dll")]
    internal static partial uint CommDlgExtendedError();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    internal static partial int GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint memory);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("user32.dll")]
    internal static partial int CloseClipboard();

    [LibraryImport("user32.dll")]
    internal static partial int GetWindowRect(nint window, Rect* bounds);

    [LibraryImport("user32.dll")]
    internal static partial int RedrawWindow(nint window, Rect* bounds, nint region, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial int PrintWindow(nint window, nint dc, uint flags);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint window, uint attribute, Rect* value, uint bytes);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    internal static partial int DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateDIBSection(nint dc, BitmapInfo* info, uint usage, out nint pixels, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    internal static partial int GdiFlush();

    [LibraryImport("gdiplus.dll")]
    internal static partial int GdiplusStartup(out nuint token, GdiPlusStartupInput* input, nint output);

    [LibraryImport("gdiplus.dll")]
    internal static partial void GdiplusShutdown(nuint token);

    [LibraryImport("gdiplus.dll")]
    internal static partial int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, nint pixels, out nint image);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GdipSaveImageToFile(nint image, string path, Guid* encoder, nint parameters);

    [LibraryImport("gdiplus.dll")]
    internal static partial int GdipDisposeImage(nint image);
}
