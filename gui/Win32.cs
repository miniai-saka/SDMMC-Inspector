using System.Runtime.InteropServices;

namespace SdmmcInspector;

internal static unsafe partial class Win32
{
    internal const uint WsOverlappedWindow = 0x00CF0000, WsClipChildren = 0x02000000;
    internal const uint WsChild = 0x40000000, WsVisible = 0x10000000, WsBorder = 0x00800000;
    internal const uint WsTabStop = 0x00010000, WsVScroll = 0x00200000, WsHScroll = 0x00100000;

    internal const uint WmCreate = 0x0001, WmDestroy = 0x0002, WmSize = 0x0005;
    internal const uint WmPaint = 0x000F, WmClose = 0x0010, WmEraseBackground = 0x0014;
    internal const uint WmSysColorChange = 0x0015, WmSettingChange = 0x001A, WmGetMinMaxInfo = 0x0024;
    internal const uint WmSetRedraw = 0x000B, WmSetFont = 0x0030, WmNotify = 0x004E;
    internal const uint WmKeyDown = 0x0100, WmInitDialog = 0x0110, WmCommand = 0x0111, WmTimer = 0x0113, WmInitMenuPopup = 0x0117;
    internal const uint WmCtlColorDialog = 0x0136, WmCtlColorStatic = 0x0138;
    internal const uint WmDpiChanged = 0x02E0, WmPrintClient = 0x0318;
    internal const uint WmAppResult = 0x8001, WmAppSelection = 0x8002;

    internal const uint CbAddString = 0x0143, CbGetCurSel = 0x0147, CbResetContent = 0x014B;
    internal const uint CbSetCurSel = 0x014E, CbSetItemHeight = 0x0153, CbGetDroppedState = 0x0157, CbSetDroppedWidth = 0x0160;
    internal const int CbnSelChange = 1, CbnCloseup = 8;
    internal const uint CcmSetVersion = 0x2007;
    internal const uint EmSetSel = 0x00B1, EmSetMargins = 0x00D3;

    internal const uint LvFirst = 0x1000, LvSetBkColor = LvFirst + 1, LvSetImageList = LvFirst + 3;
    internal const uint LvDeleteAllItems = LvFirst + 9, LvGetNextItem = LvFirst + 12;
    internal const uint LvSetColumnWidth = LvFirst + 30, LvSetTextColor = LvFirst + 36;
    internal const uint LvSetTextBkColor = LvFirst + 38, LvGetItemState = LvFirst + 44, LvSetExtendedStyle = LvFirst + 54;
    internal const uint LvInsertItem = LvFirst + 77, LvSetColumn = LvFirst + 96, LvInsertColumn = LvFirst + 97, LvSetItemText = LvFirst + 116;
    internal const uint LvExGridLines = 1, LvExFullRowSelect = 0x20, LvExInfoTip = 0x400, LvExDoubleBuffer = 0x10000;

    internal const int NmDoubleClick = -3, NmReturn = -4, NmCustomDraw = -12, LvnGetInfoTip = -158;
    internal const uint CddsPrePaint = 1, CddsItemPrePaint = 0x10001, CddsSubItem = 0x20000;
    internal const nint CdrfNotifyItemDraw = 0x20, CdrfNewFont = 2;
    internal const uint LvisSelected = 2;

    internal const uint MfPopup = 0x10, MfSeparator = 0x0800, MfGrayed = 0x0001, MfChecked = 0x0008, MfByPosition = 0x0400;
    internal const int SwHide = 0, SwShow = 5, SwShowNa = 8;
    internal const int CwUseDefault = unchecked((int)0x80000000);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect(int left, int top, int right, int bottom)
    {
        internal int Left = left, Top = top, Right = right, Bottom = bottom;
        internal readonly int Width => Right - Left;
        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MinMaxInfo
    {
        internal Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Hwnd;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Position;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowClass
    {
        internal uint Size, Style;
        internal delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background, MenuName;
        internal char* ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommonControls
    {
        internal uint Size, Classes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Paint
    {
        internal nint Dc;
        internal int Erase;
        internal Rect Clip;
        internal int Restore, Update;
        internal fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NotifyHeader
    {
        internal nint Source;
        internal nuint Id;
        internal int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ListDraw
    {
        internal NotifyHeader Header;
        internal uint Stage;
        internal nint Dc;
        internal Rect Bounds;
        internal nuint ItemIndex;
        internal uint ItemState;
        internal nint ItemParam;
        internal uint TextColor, Background;
        internal int SubItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ListInfoTip
    {
        internal NotifyHeader Header;
        internal uint Flags;
        internal char* Text;
        internal int Capacity, Item, SubItem;
        internal nint Param;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ListColumn
    {
        internal uint Mask;
        internal int Format, Width;
        internal char* Text;
        internal int TextCapacity, SubItem, Image, Order, MinWidth, DefaultWidth, IdealWidth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ListItem
    {
        internal uint Mask;
        internal int Item, SubItem;
        internal uint State, StateMask;
        internal char* Text;
        internal int TextCapacity, Image;
        internal nint Param;
        internal int Indent, GroupId;
        internal uint Columns;
        internal nint ColumnIndexes, ColumnFormats;
        internal int Group;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HighContrast
    {
        internal uint Size, Flags;
        internal nint Scheme;
    }

    [LibraryImport("__Internal", EntryPoint = "sdmmc_run_reader")]
    internal static partial int RunNativeReader();

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    internal static partial nint GetModuleHandle(nint name);

    [LibraryImport("kernel32.dll")]
    internal static partial ushort GetUserDefaultUILanguage();

    [LibraryImport("comctl32.dll")]
    internal static partial int InitCommonControlsEx(CommonControls* controls);

    [LibraryImport("comctl32.dll", EntryPoint = "ImageList_Create")]
    internal static partial nint ImageListCreate(int width, int height, uint flags, int initial, int grow);

    [LibraryImport("comctl32.dll", EntryPoint = "ImageList_Destroy")]
    internal static partial int ImageListDestroy(nint list);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(WindowClass* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWindowEx(uint extendedStyle, string className, string text, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(Message* message, nint window, uint first, uint last);

    [LibraryImport("user32.dll", EntryPoint = "IsDialogMessageW")]
    internal static partial int IsDialogMessage(nint window, Message* message);

    [LibraryImport("user32.dll")]
    internal static partial int TranslateMessage(Message* message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(Message* message);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    internal static partial int PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [LibraryImport("user32.dll")]
    internal static partial int KillTimer(nint window, nuint id);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SetWindowText(nint window, string value);

    [LibraryImport("user32.dll")]
    internal static partial nint CreateMenu();

    [LibraryImport("user32.dll")]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int AppendMenu(nint menu, uint flags, nuint item, string? text);

    [LibraryImport("user32.dll")]
    internal static partial int DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    internal static partial nint GetMenu(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int SetMenu(nint window, nint menu);

    [LibraryImport("user32.dll")]
    internal static partial int DrawMenuBar(nint window);

    [LibraryImport("user32.dll")]
    internal static partial int GetMenuItemCount(nint menu);

    [LibraryImport("user32.dll")]
    internal static partial int DeleteMenu(nint menu, uint item, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial uint EnableMenuItem(nint menu, uint item, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial uint CheckMenuItem(nint menu, uint item, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial int CheckMenuRadioItem(nint menu, uint first, uint last, uint selected, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "DialogBoxIndirectParamW", SetLastError = true)]
    internal static partial nint DialogBoxIndirectParam(nint instance, byte* template, nint owner,
        delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> procedure, nint parameter);

    [LibraryImport("user32.dll")]
    internal static partial int EndDialog(nint dialog, nint result);

    [LibraryImport("user32.dll")]
    internal static partial nint GetDlgItem(nint dialog, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetDlgItemTextW")]
    internal static partial uint GetDlgItemText(nint dialog, int id, char* buffer, int capacity);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    internal static partial nint LoadCursor(nint instance, nint name);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    internal static partial nint LoadIcon(nint instance, nint name);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW")]
    internal static partial nint LoadImage(nint instance, nint name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int metric);

    [LibraryImport("user32.dll")]
    internal static partial int ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    internal static partial int DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    internal static partial int EnableWindow(nint window, int enable);

    [LibraryImport("user32.dll")]
    internal static partial nint SetFocus(nint window);

    [LibraryImport("user32.dll")]
    internal static partial int IsWindowEnabled(nint window);

    [LibraryImport("user32.dll")]
    internal static partial int IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    internal static partial short GetKeyState(int key);

    [LibraryImport("user32.dll")]
    internal static partial int GetClientRect(nint window, Rect* bounds);

    [LibraryImport("user32.dll")]
    internal static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial int MoveWindow(nint window, int x, int y, int width, int height, int repaint);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    internal static partial int AdjustWindowRectExForDpi(Rect* bounds, uint style, int hasMenu, uint extendedStyle, uint dpi);

    [LibraryImport("user32.dll")]
    internal static partial int InvalidateRect(nint window, Rect* bounds, int erase);

    [LibraryImport("user32.dll")]
    internal static partial nint BeginPaint(nint window, Paint* paint);

    [LibraryImport("user32.dll")]
    internal static partial int EndPaint(nint window, Paint* paint);

    [LibraryImport("user32.dll")]
    internal static partial int FillRect(nint dc, Rect* bounds, nint brush);

    [LibraryImport("user32.dll")]
    internal static partial int FrameRect(nint dc, Rect* bounds, nint brush);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(nint window, string text, string title, uint type);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    internal static partial int SystemParametersInfo(uint action, uint parameter, HighContrast* value, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial uint GetSysColor(int color);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeout, uint charset, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string family);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    internal static partial int DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    internal static partial uint SetTextColor(nint dc, uint color);

    [LibraryImport("gdi32.dll")]
    internal static partial uint SetBkColor(nint dc, uint color);

    [LibraryImport("gdi32.dll")]
    internal static partial int SetBkMode(nint dc, int mode);
}
