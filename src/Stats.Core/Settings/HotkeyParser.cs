namespace Stats.Core.Settings;

/// <summary>Win32 RegisterHotKey-compatible modifiers + virtual key, plus a normalized display string.</summary>
public sealed record ParsedHotkey(uint Modifiers, uint VirtualKey, string Display);

public static class HotkeyParser
{
    public const uint ModAlt = 0x0001, ModCtrl = 0x0002, ModShift = 0x0004, ModWin = 0x0008;

    public static ParsedHotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint mods = 0;
        string? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": mods |= ModCtrl; break;
                case "SHIFT": mods |= ModShift; break;
                case "ALT": mods |= ModAlt; break;
                case "WIN": case "WINDOWS": mods |= ModWin; break;
                default:
                    if (key is not null) return null;
                    key = part.ToUpperInvariant();
                    break;
            }
        }
        if (key is null) return null;
        var vk = VirtualKeyOf(key);
        if (vk is null) return null;
        bool isFunctionKey = key.Length >= 2 && key[0] == 'F' && char.IsDigit(key[1]);
        if (mods == 0 && !isFunctionKey) return null;
        return new ParsedHotkey(mods, vk.Value, Format(mods, key));
    }

    public static string Format(uint mods, string key)
    {
        var parts = new List<string>(5);
        if ((mods & ModCtrl) != 0) parts.Add("Ctrl");
        if ((mods & ModShift) != 0) parts.Add("Shift");
        if ((mods & ModAlt) != 0) parts.Add("Alt");
        if ((mods & ModWin) != 0) parts.Add("Win");
        parts.Add(key.ToUpperInvariant());
        return string.Join("+", parts);
    }

    private static uint? VirtualKeyOf(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            return char.ToUpperInvariant(key[0]); // 'A'..'Z' = 0x41.., '0'..'9' = 0x30..
        if (key.Length >= 2 && key[0] == 'F' && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24)
            return (uint)(0x70 + f - 1);
        return key switch
        {
            "SPACE" => 0x20, "PAUSE" => 0x13, "INSERT" => 0x2D, "DELETE" => 0x2E,
            "HOME" => 0x24, "END" => 0x23, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            "SCROLLLOCK" => 0x91, "NUMLOCK" => 0x90, "TAB" => 0x09,
            _ => null,
        };
    }
}
