namespace VenToMe;

/// <summary>Small dialog for picking the two hotkeys: click a box, then press the combination.</summary>
sealed class SettingsForm : Form
{
    readonly TextBox _gatherBox = NewCaptureBox();
    readonly TextBox _undoBox = NewCaptureBox();

    public Hotkey Gather { get; private set; }
    public Hotkey Undo { get; private set; }

    public SettingsForm(Hotkey gather, Hotkey undo)
    {
        Gather = gather;
        Undo = undo;

        Text = "VenToMe hotkeys";
        Icon = AppIcon.Load();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(380, 150);

        AddRow("Gather windows here:", _gatherBox, 15, () => Gather, h => Gather = h);
        AddRow("Send windows back:", _undoBox, 55, () => Undo, h => Undo = h);

        var hint = new Label { Text = "Click a box and press the combination (needs Ctrl, Alt, Shift or Win).",
            Left = 15, Top = 90, Width = 350, Height = 18 };
        var reset = new Button { Text = "Defaults", Left = 15, Top = 112, Width = 80 };
        reset.Click += (_, _) =>
        {
            Gather = Hotkey.DefaultGather; Undo = Hotkey.DefaultUndo;
            _gatherBox.Text = Gather.ToString(); _undoBox.Text = Undo.ToString();
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 215, Top = 112, Width = 70 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 295, Top = 112, Width = 70 };
        Controls.AddRange([hint, reset, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    static TextBox NewCaptureBox() => new() { ReadOnly = true, Left = 150, Width = 215, ShortcutsEnabled = false };

    void AddRow(string label, TextBox box, int top, Func<Hotkey> get, Action<Hotkey> set)
    {
        box.Top = top;
        box.Text = get().ToString();
        Controls.Add(new Label { Text = label, Left = 15, Top = top + 3, Width = 130 });
        Controls.Add(box);

        box.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = e.Handled = true;
            if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin) return; // wait for the real key

            uint mods = 0;
            if (e.Control) mods |= NativeMethods.MOD_CONTROL;
            if (e.Alt) mods |= NativeMethods.MOD_ALT;
            if (e.Shift) mods |= NativeMethods.MOD_SHIFT;
            // WinForms' ModifierKeys/KeyEventArgs don't report the Win key, so poll it directly.
            if (IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN)) mods |= NativeMethods.MOD_WIN;
            if (mods == 0) return; // a bare key would hijack normal typing

            var hotkey = new Hotkey(mods, e.KeyCode);
            set(hotkey);
            box.Text = hotkey.ToString();
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && Gather == Undo)
        {
            MessageBox.Show(this, "The two hotkeys must be different.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }
        base.OnFormClosing(e);
    }
}
