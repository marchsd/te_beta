using System.Globalization;
using System.Text;

namespace DesktopWorld.Core;

/// <summary>Configurable bindings as Win32 virtual-key codes (mouse buttons use VK_LBUTTON=1, VK_RBUTTON=2, VK_MBUTTON=4).
/// File format: one "action = Key" per line, '#' comments. Unknown actions/keys are reported, never fatal.</summary>
public sealed class ControlsConfig
{
    public int Left = 'A', Right = 'D', Jump = 0x20, Up = 'W', Down = 'S';
    public int Mine = 0x01;      // left mouse button (hold on a target in reach)
    public int Attack = 0x02;    // right mouse button
    public int Spawn = 'G';      // spawn a slime
    public int Leave = 0x1B;     // Escape: leave gameplay mode
    public int ToggleKey = 'G';  // global hotkey = Ctrl+Alt+<key> toggles gameplay mode
    public int QuitKey = 'Q';    // global hotkey = Ctrl+Alt+<key> quits the companion

    private static readonly Dictionary<string, int> Names = BuildNames();

    private static Dictionary<string, int> BuildNames()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = 0x20, ["Escape"] = 0x1B, ["Esc"] = 0x1B, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Shift"] = 0x10,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["LMB"] = 0x01, ["RMB"] = 0x02, ["MMB"] = 0x04,
        };
        for (char c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) d[c.ToString()] = c;
        for (int i = 1; i <= 12; i++) d["F" + i] = 0x6F + i;
        return d;
    }

    public static bool TryParseKey(string name, out int vk) => Names.TryGetValue(name.Trim(), out vk);

    public static string KeyName(int vk) => Names.FirstOrDefault(kv => kv.Value == vk && kv.Key != "Esc").Key ?? $"0x{vk:X2}";

    public static ControlsConfig Parse(string text, out List<string> problems)
    {
        var cfg = new ControlsConfig(); problems = new List<string>();
        int lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim().TrimEnd('\r');
            int hash = line.IndexOf('#'); if (hash >= 0) line = line.Substring(0, hash).Trim();
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) { problems.Add($"line {lineNo}: expected 'action = key'"); continue; }
            string action = line.Substring(0, eq).Trim().ToLowerInvariant(), key = line.Substring(eq + 1).Trim();
            if (!TryParseKey(key, out int vk)) { problems.Add($"line {lineNo}: unknown key '{key}'"); continue; }
            switch (action)
            {
                case "left": cfg.Left = vk; break;
                case "right": cfg.Right = vk; break;
                case "jump": cfg.Jump = vk; break;
                case "up": cfg.Up = vk; break;
                case "down": cfg.Down = vk; break;
                case "mine": cfg.Mine = vk; break;
                case "attack": cfg.Attack = vk; break;
                case "spawn": cfg.Spawn = vk; break;
                case "leave": cfg.Leave = vk; break;
                case "togglehotkey": cfg.ToggleKey = vk; break;
                case "quithotkey": cfg.QuitKey = vk; break;
                default: problems.Add($"line {lineNo}: unknown action '{action}'"); break;
            }
        }
        var dup = cfg.GameplayKeys().GroupBy(k => k.vk).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) problems.Add($"warning: key {KeyName(dup.Key)} is bound to several actions ({string.Join(", ", dup.Select(d => d.name))})");
        return cfg;
    }

    public IEnumerable<(string name, int vk)> GameplayKeys() => new[]
    {
        ("left", Left), ("right", Right), ("jump", Jump), ("up", Up), ("down", Down), ("mine", Mine), ("attack", Attack), ("spawn", Spawn), ("leave", Leave),
    };

    public string ToIni()
    {
        var sb = new StringBuilder("# Desktop World controls. Keys: A-Z, 0-9, F1-F12, Space, Escape, Enter, Tab, Shift, Left/Up/Right/Down, LMB, RMB, MMB\n");
        foreach (var (n, vk) in GameplayKeys()) sb.Append(n).Append(" = ").Append(KeyName(vk)).Append('\n');
        sb.Append("# Global hotkeys are Ctrl+Alt+<key>\n");
        sb.Append("togglehotkey = ").Append(KeyName(ToggleKey)).Append('\n').Append("quithotkey = ").Append(KeyName(QuitKey)).Append('\n');
        return sb.ToString();
    }
}
