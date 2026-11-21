using System.Globalization;
using System.Text;
using static SdmmcInspector.Win32;

namespace SdmmcInspector;

internal static class NativeDialogTemplate
{
    internal const ushort ButtonClass = 0x0080, EditClass = 0x0081, StaticClass = 0x0082;
    internal const uint StaticStyle = WsChild | WsVisible;
    internal const uint ButtonStyle = WsChild | WsVisible | WsTabStop;
    internal const uint EditStyle = WsChild | WsVisible | WsTabStop | 0x2080; // ES_NUMBER | ES_AUTOHSCROLL
    internal const uint ClientEdge = 0x0200;

    internal readonly record struct Control(int Id, ushort Class, uint Style, short X, short Y, short Width, short Height,
        string Caption = "", uint ExtendedStyle = 0);

    internal static byte[] Build(string title, short width, short height, string fontFamily, ReadOnlySpan<Control> controls)
    {
        using var storage = new MemoryStream();
        using var writer = new BinaryWriter(storage);
        writer.Write((ushort)1);
        writer.Write((ushort)0xFFFF); // DLGTEMPLATEEX signature
        writer.Write(0u); // Help ID
        writer.Write(0u); // Extended style
        writer.Write(0x80C808C0u); // WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_CENTER | DS_MODALFRAME | DS_SETFONT
        writer.Write((ushort)controls.Length);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)0); // No menu
        writer.Write((ushort)0); // Default dialog class
        WriteWideString(writer, title);
        writer.Write((ushort)10);
        writer.Write((ushort)400);
        writer.Write((byte)0);
        writer.Write((byte)1);
        WriteWideString(writer, fontFamily);

        foreach (Control control in controls)
        {
            while (storage.Position % 4 != 0)
                writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(control.ExtendedStyle);
            writer.Write(control.Style);
            writer.Write(control.X);
            writer.Write(control.Y);
            writer.Write(control.Width);
            writer.Write(control.Height);
            writer.Write(control.Id);
            writer.Write((ushort)0xFFFF);
            writer.Write(control.Class);
            WriteWideString(writer, control.Caption);
            writer.Write((ushort)0); // No creation data
        }
        return storage.ToArray();
    }

    internal static unsafe int ReadTwoDigits(nint window, int id)
    {
        Span<char> value = stackalloc char[12];
        fixed (char* address = value)
        {
            int length = (int)GetDlgItemText(window, id, address, value.Length);
            if (length is 1 or 2 && int.TryParse(value[..length], NumberStyles.None, CultureInfo.InvariantCulture, out int result))
                return result;
        }
        return -1;
    }

    private static void WriteWideString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.Unicode.GetBytes(value));
        writer.Write((ushort)0);
    }
}
