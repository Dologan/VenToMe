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

/// <summary>Systray icon + configurable global hotkeys to gather every window to the current monitor and send them back.</summary>
sealed class TrayContext : ApplicationContext
{
    const int GatherId = 1, UndoId = 2;
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValueName = "VenToMe";

    readonly NotifyIcon _tray;
    readonly HotkeyWindow _hotkeyWindow;
    readonly ToolStripMenuItem _autostartItem;
    readonly ToolStripMenuItem _gatherItem;
    readonly ToolStripMenuItem _undoItem;
    readonly Settings _settings = Settings.Load();

    // Original placement of each window moved by the last gather(s); restored by Undo.
    readonly Dictionary<IntPtr, NativeMethods.WINDOWPLACEMENT> _undo = new();

    public TrayContext()
    {
        _autostartItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = IsAutostart() };
        _autostartItem.CheckedChanged += (_, _) => SetAutostart(_autostartItem.Checked);

        var menu = new ContextMenuStrip();
        _gatherItem = (ToolStripMenuItem)menu.Items.Add("", null, (_, _) => Gather());
        _undoItem = (ToolStripMenuItem)menu.Items.Add("", null, (_, _) => Undo());
        menu.Items.Add("Hotkeys...", null, (_, _) => ShowSettings());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Gather(); };

        _hotkeyWindow = new HotkeyWindow(id => { if (id == GatherId) Gather(); else Undo(); });
        RegisterHotkeys(showWarning: true);
    }

    protected override void ExitThreadCore()
    {
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, GatherId);
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, UndoId);
        _hotkeyWindow.DestroyHandle();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }

    /// <summary>(Re)registers both hotkeys from the settings and refreshes the menu/tooltip text.</summary>
    /// <returns>False if either hotkey was already taken by another program.</returns>
    bool RegisterHotkeys(bool showWarning)
    {
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, GatherId);
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, UndoId);

        var gather = _settings.GatherHotkey;
        var undo = _settings.UndoHotkey;
        _gatherItem.Text = $"Gather windows here ({gather})";
        _undoItem.Text = $"Send windows back ({undo})";
        _tray.Text = $"VenToMe - {gather} gathers windows";

        var failed = new List<string>();
        if (!NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, GatherId, gather.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)gather.Key))
            failed.Add(gather.ToString());
        if (!NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, UndoId, undo.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)undo.Key))
            failed.Add(undo.ToString());

        if (failed.Count > 0 && showWarning)
        {
            _tray.ShowBalloonTip(5000, "VenToMe",
                $"Could not register {string.Join(" and ", failed)}; another program is using it. The tray menu still works.",
                ToolTipIcon.Warning);
        }
        return failed.Count == 0;
    }

    void ShowSettings()
    {
        using var form = new SettingsForm(_settings.GatherHotkey, _settings.UndoHotkey);
        if (form.ShowDialog() != DialogResult.OK) return;

        _settings.Gather = form.Gather.ToString();
        _settings.Undo = form.Undo.ToString();
        try { _settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Hotkeys are active but could not be saved:\n{ex.Message}", "VenToMe",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RegisterHotkeys(showWarning: true);
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
    void Gather()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var targetMon = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var target = MonitorWorkArea(targetMon);
        uint self = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsMovable(hwnd, self) && MoveToMonitor(hwnd, targetMon, target, out var original))
                _undo.TryAdd(hwnd, original); // keep the first original if gathered repeatedly
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Puts every window moved by Gather back where it was (position, size and min/max state).</summary>
    void Undo()
    {
        foreach (var (hwnd, original) in _undo)
        {
            if (!NativeMethods.IsWindow(hwnd)) continue;
            var wp = original;
            NativeMethods.SetWindowPlacement(hwnd, ref wp);
        }
        _undo.Clear();
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

    static bool MoveToMonitor(IntPtr hwnd, IntPtr targetMon, NativeMethods.RECT target,
        out NativeMethods.WINDOWPLACEMENT original)
    {
        var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        original = default;
        if (!NativeMethods.GetWindowPlacement(hwnd, ref wp)) return false;
        original = wp;

        // rcNormalPosition is in workspace coordinates (excludes taskbars), valid for min/max/normal alike.
        var rect = wp.rcNormalPosition;
        var srcMon = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (srcMon == targetMon) return false;

        var src = MonitorWorkArea(srcMon);
        int w = Math.Min(rect.Width, target.Width);
        int h = Math.Min(rect.Height, target.Height);
        // Keep the window's relative offset within its monitor, clamped to fit on the target.
        int x = Math.Clamp(target.left + (rect.left - src.left), target.left, target.right - w);
        int y = Math.Clamp(target.top + (rect.top - src.top), target.top, target.bottom - h);

        wp.rcNormalPosition = new NativeMethods.RECT { left = x, top = y, right = x + w, bottom = y + h };
        return NativeMethods.SetWindowPlacement(hwnd, ref wp);
    }

    static NativeMethods.RECT MonitorWorkArea(IntPtr monitor)
    {
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }

    sealed class HotkeyWindow : NativeWindow
    {
        readonly Action<int> _onHotkey;

        public HotkeyWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY) _onHotkey((int)m.WParam);
            else base.WndProc(ref m);
        }
    }
}

/// <summary>Loads the embedded app.ico at the system small-icon size (crisp in the tray at any DPI).</summary>
static class AppIcon
{
    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, SystemInformation.SmallIconSize);
    }
}

static class NativeMethods
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    public const uint GW_OWNER = 4;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int DWMWA_CLOAKED = 14;
    public const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

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
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
}
