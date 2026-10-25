using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VenToMe;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\VenToMe.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
    }
}

/// <summary>Systray icon + global hotkey (Ctrl+Alt+G) that gathers every window to the current monitor.</summary>
sealed class TrayContext : ApplicationContext
{
    const int HotkeyId = 1;
    const uint Modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
    const uint Key = (uint)Keys.G;
    const string HotkeyText = "Ctrl+Alt+G";
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValueName = "VenToMe";

    readonly NotifyIcon _tray;
    readonly HotkeyWindow _hotkeyWindow;
    readonly ToolStripMenuItem _autostartItem;

    public TrayContext()
    {
        _autostartItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = IsAutostart() };
        _autostartItem.CheckedChanged += (_, _) => SetAutostart(_autostartItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add($"Gather windows here ({HotkeyText})", null, (_, _) => Gather());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"VenToMe - {HotkeyText} gathers windows",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Gather(); };

        _hotkeyWindow = new HotkeyWindow(Gather);
        if (!NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, Modifiers, Key))
        {
            _tray.ShowBalloonTip(5000, "VenToMe",
                $"Could not register {HotkeyText}; another program is using it. The tray icon still works.",
                ToolTipIcon.Warning);
        }
    }

    protected override void ExitThreadCore()
    {
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _hotkeyWindow.DestroyHandle();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }

    static bool IsAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    static void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enable) key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunValueName, false);
    }

    /// <summary>Moves every top-level window not on the monitor under the cursor onto it.</summary>
    static void Gather()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var targetMon = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var target = MonitorWorkArea(targetMon);
        uint self = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsMovable(hwnd, self)) MoveToMonitor(hwnd, targetMon, target);
            return true;
        }, IntPtr.Zero);
    }

    static bool IsMovable(IntPtr hwnd, uint selfPid)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;
        if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return false;
        if (NativeMethods.GetWindowTextLength(hwnd) == 0) return false;

        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (ex & NativeMethods.WS_EX_APPWINDOW) == 0) return false;

        // Skip windows on other virtual desktops / hidden UWP shells.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid != selfPid;
    }

    static void MoveToMonitor(IntPtr hwnd, IntPtr targetMon, NativeMethods.RECT target)
    {
        var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(hwnd, ref wp)) return;

        // rcNormalPosition is in workspace coordinates (excludes taskbars), valid for min/max/normal alike.
        var rect = wp.rcNormalPosition;
        var srcMon = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (srcMon == targetMon) return;

        var src = MonitorWorkArea(srcMon);
        int w = Math.Min(rect.Width, target.Width);
        int h = Math.Min(rect.Height, target.Height);
        // Keep the window's relative offset within its monitor, clamped to fit on the target.
        int x = Math.Clamp(target.left + (rect.left - src.left), target.left, target.right - w);
        int y = Math.Clamp(target.top + (rect.top - src.top), target.top, target.bottom - h);

        wp.rcNormalPosition = new NativeMethods.RECT { left = x, top = y, right = x + w, bottom = y + h };
        NativeMethods.SetWindowPlacement(hwnd, ref wp);
    }

    static NativeMethods.RECT MonitorWorkArea(IntPtr monitor)
    {
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }

    sealed class HotkeyWindow : NativeWindow
    {
        readonly Action _onHotkey;

        public HotkeyWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && (int)m.WParam == HotkeyId) _onHotkey();
            else base.WndProc(ref m);
        }
    }
}

static class NativeMethods
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    public const uint GW_OWNER = 4;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left, top, right, bottom;
        public readonly int Width => right - left;
        public readonly int Height => bottom - top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public uint flags;
        public uint showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT rc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
}
