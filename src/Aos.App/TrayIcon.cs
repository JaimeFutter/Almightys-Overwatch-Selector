using System.Runtime.InteropServices;

namespace Aos.App;

/// <summary>
/// A notification-area (system tray) icon. WinUI 3 has no tray API, so this uses Shell_NotifyIcon and
/// subclasses the main window to receive the icon's mouse messages. Balloon notifications from here
/// show up as regular Windows 11 notifications, and unlike AppNotificationManager they also work when
/// the app runs as administrator.
/// </summary>
internal sealed partial class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x10; // WM_APP + 16
    private const uint IconId = 1;
    private const int CmdOpen = 1, CmdExit = 2;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _smallIcon, _largeIcon;
    private readonly uint _taskbarCreated;
    private readonly SubclassProc _proc; // kept alive: native code holds a pointer to it
    private bool _added;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayIcon(IntPtr hwnd, string iconPath, string tooltip)
    {
        _hwnd = hwnd;
        Tooltip = tooltip;
        _smallIcon = LoadImageW(IntPtr.Zero, iconPath, ImageIcon, GetSystemMetrics(SmCxSmIcon), GetSystemMetrics(SmCySmIcon), LrLoadFromFile);
        _largeIcon = LoadImageW(IntPtr.Zero, iconPath, ImageIcon, 64, 64, LrLoadFromFile);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _proc = WndProc;
        SetWindowSubclass(_hwnd, _proc, (UIntPtr)0xA05, UIntPtr.Zero);
        Add();
    }

    public string Tooltip { get; }

    /// <summary>Shows a notification attached to the tray icon.</summary>
    public void Notify(string title, string message)
    {
        var data = NewData(NifInfo);
        data.szInfoTitle = title;
        data.szInfo = message;
        data.dwInfoFlags = NiifUser | NiifLargeIcon;
        data.hBalloonIcon = _largeIcon;
        Shell_NotifyIconW(NimModify, ref data);
    }

    private void Add()
    {
        var data = NewData(NifMessage | NifIcon | NifTip | NifShowTip);
        _added = Shell_NotifyIconW(NimAdd, ref data);
    }

    private NotifyIconData NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _smallIcon,
        szTip = Tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (msg == CallbackMessage)
        {
            switch ((uint)lParam.ToInt64() & 0xFFFF)
            {
                case WmLButtonUp:
                case WmLButtonDblClk:
                case NinBalloonUserClick:
                    OpenRequested?.Invoke();
                    break;
                case WmRButtonUp:
                    ShowMenu();
                    break;
            }
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0)
            Add(); // Explorer restarted: the icon has to be added again.
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        AppendMenuW(menu, MfString, (UIntPtr)CmdOpen, "Open");
        AppendMenuW(menu, MfSeparator, UIntPtr.Zero, null);
        AppendMenuW(menu, MfString, (UIntPtr)CmdExit, "Exit");
        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd); // otherwise the menu doesn't close when clicking elsewhere
        int cmd = TrackPopupMenu(menu, TpmReturnCmd | TpmRightButton | TpmNoNotify, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);
        if (cmd == CmdOpen) OpenRequested?.Invoke();
        else if (cmd == CmdExit) ExitRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIconW(NimDelete, ref data);
            _added = false;
        }
        RemoveWindowSubclass(_hwnd, _proc, (UIntPtr)0xA05);
        if (_smallIcon != IntPtr.Zero) DestroyIcon(_smallIcon);
        if (_largeIcon != IntPtr.Zero) DestroyIcon(_largeIcon);
    }

    // ----- Win32 -----

    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10, NifShowTip = 0x80;
    private const uint NiifUser = 0x4, NiifLargeIcon = 0x20;
    private const uint WmLButtonUp = 0x0202, WmLButtonDblClk = 0x0203, WmRButtonUp = 0x0205, NinBalloonUserClick = 0x0405;
    private const uint MfString = 0x0, MfSeparator = 0x800;
    private const uint TpmRightButton = 0x2, TpmNoNotify = 0x80, TpmReturnCmd = 0x100;
    private const uint ImageIcon = 1, LrLoadFromFile = 0x10;
    private const int SmCxSmIcon = 49, SmCySmIcon = 50;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point pt);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
