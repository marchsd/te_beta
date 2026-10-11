using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using Terraria;

namespace OverlaySpike
{
    /// <summary>Reflection dump of Terraria's draw code so patch targets are READ from the real assembly, not guessed.</summary>
    internal static class Dump
    {
        private static readonly Dictionary<short, OpCode> Ops = BuildOps();
        private static readonly Regex Interesting = new Regex("Draw|Background|BG|Clear|Black|Sky|Cloud|Sun|Moon|Star|Wall|Tile|Light|Render|Water|Liquid", RegexOptions.IgnoreCase);

        private static Dictionary<short, OpCode> BuildOps()
        {
            var d = new Dictionary<short, OpCode>();
            foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.FieldType == typeof(OpCode)) { OpCode o = (OpCode)f.GetValue(null); d[o.Value] = o; }
            }
            return d;
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            Assembly asm = typeof(Main).Assembly;
            sb.AppendLine("Terraria assembly: " + asm.FullName);
            sb.AppendLine("Terraria.Main.versionNumber = " + SafeVersion());
            sb.AppendLine();

            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            sb.AppendLine("=== Main: methods matching draw-related names ===");
            foreach (MethodInfo m in typeof(Main).GetMethods(All))
                if (Interesting.IsMatch(m.Name)) sb.AppendLine("  " + Sig(m));

            sb.AppendLine();
            sb.AppendLine("=== Types with Tile/Wall/Background/Lighting in the name (methods named like Draw/Render/Update) ===");
            foreach (Type t in SafeTypes(asm))
            {
                string n = t.FullName ?? t.Name;
                if (!Regex.IsMatch(n, "TileDrawing|WallDrawing|Background|Lighting|LiquidRenderer|WorldGen$", RegexOptions.IgnoreCase)) continue;
                sb.AppendLine("[" + n + "]");
                foreach (MethodInfo m in t.GetMethods(All))
                    if (Regex.IsMatch(m.Name, "Draw|Render|Update|Clear|Black|Light", RegexOptions.IgnoreCase)) sb.AppendLine("  " + Sig(m));
            }

            sb.AppendLine();
            sb.AppendLine("=== Call order inside Main.DoDraw (IL order, depth 2) ===");
            foreach (MethodInfo m in typeof(Main).GetMethods(All))
            {
                if (m.Name != "DoDraw") continue;
                sb.AppendLine(Sig(m));
                Callees(m, 1, 2, new HashSet<MethodBase>(), sb);
            }
            return sb.ToString();
        }

        private static string SafeVersion()
        {
            try { return Main.versionNumber; } catch { return "(unavailable)"; }
        }

        private static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { var l = new List<Type>(); foreach (Type t in e.Types) if (t != null) l.Add(t); return l; }
        }

        private static string Sig(MethodBase m)
        {
            var sb = new StringBuilder();
            sb.Append(m.IsStatic ? "static " : "").Append(m.DeclaringType != null ? m.DeclaringType.Name + "." : "").Append(m.Name).Append('(');
            ParameterInfo[] ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ps[i].ParameterType.Name).Append(' ').Append(ps[i].Name); }
            return sb.Append(')').ToString();
        }

        private static void Callees(MethodBase m, int depth, int maxDepth, HashSet<MethodBase> seen, StringBuilder sb)
        {
            if (!seen.Add(m)) return;
            byte[] il;
            try { MethodBody body = m.GetMethodBody(); il = body == null ? null : body.GetILAsByteArray(); }
            catch { return; }
            if (il == null) return;
            Module mod = m.Module;
            string pad = new string(' ', depth * 2);
            int i = 0;
            while (i < il.Length)
            {
                short val;
                if (il[i] == 0xFE) { if (i + 1 >= il.Length) break; val = (short)(0xFE00 | il[i + 1]); i += 2; }
                else { val = il[i]; i++; }
                OpCode op;
                if (!Ops.TryGetValue(val, out op)) break;
                int size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch:
                        if (i + 4 > il.Length) return;
                        size = 4 + 4 * BitConverter.ToInt32(il, i); break;
                    default: size = 4; break;
                }
                if (op.OperandType == OperandType.InlineMethod && i + 4 <= il.Length && (op == OpCodes.Call || op == OpCodes.Callvirt))
                {
                    try
                    {
                        MethodBase callee = mod.ResolveMethod(BitConverter.ToInt32(il, i));
                        string name = callee.DeclaringType != null ? callee.DeclaringType.FullName : "?";
                        bool ours = name != null && name.StartsWith("Terraria");
                        if (ours)
                        {
                            sb.AppendLine(pad + "-> " + Sig(callee));
                            if (depth < maxDepth && Interesting.IsMatch(callee.Name)) Callees(callee, depth + 1, maxDepth, seen, sb);
                        }
                    }
                    catch { }
                }
                i += size;
            }
        }

        public static string WriteFile()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OverlaySpike-dump.txt");
            File.WriteAllText(path, Run());
            return path;
        }
    }
}
