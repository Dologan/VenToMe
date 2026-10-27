using System.Text.Json;

namespace VenToMe;

/// <summary>A global hotkey: modifier flags (MOD_*) plus a key. Round-trips through text like "Ctrl+Alt+G".</summary>
readonly record struct Hotkey(uint Modifiers, Keys Key)
{
    public static readonly Hotkey DefaultGather = new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_WIN, Keys.G);
    public static readonly Hotkey DefaultUndo = new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_WIN, Keys.Z);

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0;
        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return false;
        foreach (var token in tokens[..^1])
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl": mods |= NativeMethods.MOD_CONTROL; break;
                case "alt": mods |= NativeMethods.MOD_ALT; break;
                case "shift": mods |= NativeMethods.MOD_SHIFT; break;
                case "win": mods |= NativeMethods.MOD_WIN; break;
                default: return false;
            }
        }
        if (!Enum.TryParse<Keys>(tokens[^1], true, out var key) || key == Keys.None) return false;

        hotkey = new Hotkey(mods, key);
        return true;
    }
}

/// <summary>User settings, stored as JSON in %AppData%\VenToMe\settings.json.</summary>
sealed class Settings
{
    static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VenToMe", "settings.json");

    public string Gather { get; set; } = Hotkey.DefaultGather.ToString();
    public string Undo { get; set; } = Hotkey.DefaultUndo.ToString();

    public Hotkey GatherHotkey => Hotkey.TryParse(Gather, out var h) ? h : Hotkey.DefaultGather;
    public Hotkey UndoHotkey => Hotkey.TryParse(Undo, out var h) ? h : Hotkey.DefaultUndo;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
