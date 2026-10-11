#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DesktopWorld.Protocol
{
    public static class Codec
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Encode(Msg m)
        {
            var f = new List<string>();
            string type;
            var hello = m as HelloMsg; var welc = m as WelcomeMsg; var ping = m as PingMsg; var pong = m as PongMsg;
            var geo = m as GeometryMsg; var mode = m as ModeMsg; var mine = m as MineMsg; var spawn = m as SpawnMsg;
            var ps = m as PlayerStateMsg; var err = m as ErrorMsg;

            if (hello != null) { type = "HELLO"; f.Add(Token(hello.Role)); f.Add(Token(hello.AppVersion)); }
            else if (welc != null) { type = "WELC"; f.Add(Token(welc.AppVersion)); }
            else if (ping != null) { type = "PING"; f.Add(ping.TimestampMs.ToString(Inv)); }
            else if (pong != null) { type = "PONG"; f.Add(pong.TimestampMs.ToString(Inv)); }
            else if (geo != null)
            {
                type = "GEOM";
                f.Add(geo.WorldVersion.ToString(Inv)); f.Add(geo.PixelsPerWorldUnit.ToString("R", Inv));
                f.Add(geo.OriginX.ToString(Inv)); f.Add(geo.OriginY.ToString(Inv));
                f.Add(Rects(geo.Solids)); f.Add(Rects(geo.Interiors));
            }
            else if (mode != null) { type = "MODE"; f.Add(mode.Active ? "1" : "0"); f.Add(mode.Paused ? "1" : "0"); }
            else if (mine != null) { type = "MINE"; f.Add(mine.X.ToString(Inv)); f.Add(mine.Y.ToString(Inv)); }
            else if (spawn != null) { type = "SPAWN"; f.Add(Token(spawn.Kind)); f.Add(spawn.X.ToString(Inv)); f.Add(spawn.Y.ToString(Inv)); }
            else if (ps != null)
            {
                type = "PSTATE";
                f.Add(ps.Hp.ToString(Inv)); f.Add(ps.MaxHp.ToString(Inv)); f.Add(ps.X.ToString(Inv)); f.Add(ps.Y.ToString(Inv)); f.Add(ps.InInterior ? "1" : "0");
            }
            else if (err != null) { type = "ERR"; f.Add(Token(err.Code)); f.Add(Sanitize(err.Text)); }
            else if (m is ByeMsg) type = "BYE";
            else throw new ArgumentException("Unsupported message type " + (m == null ? "null" : m.GetType().Name));

            var sb = new StringBuilder();
            sb.Append(Wire.Version).Append('|').Append(type).Append('|').Append(m.Seq.ToString(Inv));
            foreach (var s in f) sb.Append('|').Append(s);
            return sb.ToString();
        }

        public static bool TryDecode(string line, out Msg msg, out string error)
        {
            msg = null; error = null;
            if (line == null) { error = "null line"; return false; }
            if (line.Length == 0) { error = "empty line"; return false; }
            if (line.Length > Wire.MaxLineBytes) { error = "line too long"; return false; }
            foreach (char c in line) if (c < 32 || c > 126) { error = "non-ASCII or control character"; return false; }

            var p = line.Split('|');
            if (p.Length < 3) { error = "too few fields"; return false; }
            if (p[0] != Wire.Version) { error = "unsupported protocol version '" + Trunc(p[0]) + "'"; return false; }
            long seq;
            if (!long.TryParse(p[2], NumberStyles.Integer, Inv, out seq) || seq < 0) { error = "bad sequence number"; return false; }

            try
            {
                switch (p[1])
                {
                    case "HELLO":
                        Need(p, 5); msg = new HelloMsg { Role = Tok(p[3]), AppVersion = Tok(p[4]) }; break;
                    case "WELC":
                        Need(p, 4); msg = new WelcomeMsg { AppVersion = Tok(p[3]) }; break;
                    case "PING": Need(p, 4); msg = new PingMsg { TimestampMs = L(p[3]) }; break;
                    case "PONG": Need(p, 4); msg = new PongMsg { TimestampMs = L(p[3]) }; break;
                    case "GEOM":
                        Need(p, 9);
                        var g = new GeometryMsg { WorldVersion = I(p[3]), PixelsPerWorldUnit = D(p[4]), OriginX = I(p[5]), OriginY = I(p[6]) };
                        if (!(g.PixelsPerWorldUnit > 0)) throw new FormatException("scale must be > 0");
                        g.Solids = ParseRects(p[7]); g.Interiors = ParseRects(p[8]);
                        msg = g; break;
                    case "MODE": Need(p, 5); msg = new ModeMsg { Active = B(p[3]), Paused = B(p[4]) }; break;
                    case "MINE": Need(p, 5); msg = new MineMsg { X = I(p[3]), Y = I(p[4]) }; break;
                    case "SPAWN": Need(p, 6); msg = new SpawnMsg { Kind = Tok(p[3]), X = I(p[4]), Y = I(p[5]) }; break;
                    case "PSTATE":
                        Need(p, 8);
                        msg = new PlayerStateMsg { Hp = I(p[3]), MaxHp = I(p[4]), X = I(p[5]), Y = I(p[6]), InInterior = B(p[7]) }; break;
                    case "ERR": Need(p, 5); msg = new ErrorMsg { Code = Tok(p[3]), Text = p[4] }; break;
                    case "BYE": msg = new ByeMsg(); break;
                    default: msg = new UnknownMsg { Type = Trunc(p[1]) }; break;
                }
            }
            catch (FormatException ex) { msg = null; error = ex.Message; return false; }
            catch (OverflowException) { msg = null; error = "number out of range"; return false; }

            msg.Seq = seq;
            return true;
        }

        // ---- helpers -------------------------------------------------------------------------------------------
        private static void Need(string[] p, int n) { if (p.Length != n) throw new FormatException("wrong field count (" + p.Length + ", expected " + n + ")"); }
        private static int I(string s) { return int.Parse(s, NumberStyles.Integer, Inv); }
        private static long L(string s) { return long.Parse(s, NumberStyles.Integer, Inv); }
        private static double D(string s)
        {
            double d = double.Parse(s, NumberStyles.Float, Inv);
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException("non-finite number");
            return d;
        }
        private static bool B(string s) { if (s == "1") return true; if (s == "0") return false; throw new FormatException("bad boolean"); }
        private static string Trunc(string s) { return s.Length > 24 ? s.Substring(0, 24) : s; }

        /// <summary>Tokens: letters, digits, '.', '-', '_' only (so they can never break framing).</summary>
        private static string Token(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) && c < 128 || c == '.' || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
        private static string Tok(string s)
        {
            if (s.Length == 0 || s.Length > 64) throw new FormatException("bad token length");
            foreach (char c in s) if (!(char.IsLetterOrDigit(c) && c < 128 || c == '.' || c == '-' || c == '_')) throw new FormatException("bad token");
            return s;
        }
        private static string Sanitize(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s) { if (sb.Length >= 200) break; sb.Append(c < 32 || c > 126 || c == '|' ? ' ' : c); }
            return sb.ToString();
        }

        private static string Rects(List<Rect> list)
        {
            if (list == null || list.Count == 0) return "";
            if (list.Count > Wire.MaxListItems) throw new ArgumentException("too many rects");
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(';'); sb.Append(list[i].ToString()); }
            return sb.ToString();
        }

        private static List<Rect> ParseRects(string s)
        {
            var result = new List<Rect>();
            if (s.Length == 0) return result;
            var items = s.Split(';');
            if (items.Length > Wire.MaxListItems) throw new FormatException("too many list items");
            foreach (var it in items)
            {
                var q = it.Split(',');
                if (q.Length != 4) throw new FormatException("bad rect");
                result.Add(new Rect(I(q[0]), I(q[1]), I(q[2]), I(q[3])));
            }
            return result;
        }
    }
}
