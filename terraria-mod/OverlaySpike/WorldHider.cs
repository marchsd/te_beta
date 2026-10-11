using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TerrariaModder.Core.Logging;

namespace OverlaySpike
{
    /// <summary>
    /// Skips Terraria's tile / wall / sky / background drawing and turns the game's opaque-black screen clears into
    /// transparent ones, so only players, NPCs, projectiles, dust and UI remain.
    /// Method names below were READ from the 1.4.5.8 dump (OverlaySpike-dump.txt), not guessed.
    /// Anything that can't be found is logged and skipped; nothing here throws into the game.
    /// </summary>
    internal static class WorldHider
    {
        public static volatile bool Active;
        public static volatile bool Wiped;          // once true, saving the world is blocked for the rest of this game session
        public static volatile bool CameraLock;
        public static Vector2 CameraPx;
        public static bool SaveGuardInstalled;
        private static Harmony _h;
        private static ILogger _log;

        private static readonly string[] MainMethodsToSkip =
        {
            "DrawBG", "DrawBackgroundBlackFill", "DrawBackground",
            "DrawStarsInBackground", "DrawSunAndMoon",
            "DoDraw_WallsAndBlacks", "DoDraw_Tiles_Solid", "DoDraw_Tiles_NonSolid", "DoDraw_Waterfalls",
            "DrawWaters", "DrawLensFlare",
            // UI to remove (hotbar DrawInterface_30 and inventory DrawInterface_27 are deliberately kept)
            "DrawInterface_25_ResourceBars", "DrawInterface_16_MapOrMinimap", "DrawInterface_28_InfoAccs",
            "DrawInterface_15_InvasionProgressBars", "DrawInterface_14_EntityHealthBars", "DrawInterface_7_TownNPCHouseBanners",
            "DrawInterface_20_MultiplayerPlayerNames", "DrawInterface_19_SignTileBubble", "DrawInterface_29_SettingsButton",
            "DrawInterface_29B_ControlHints", "DrawInfoAccs", "DrawFPS", "DrawMap"
        };

        public static void Install(ILogger log)
        {
            _log = log;
            if (_h != null) return;
            _h = new Harmony("com.solyn.overlayspike");
            var skip = new HarmonyMethod(typeof(WorldHider).GetMethod("SkipPrefix", BindingFlags.Static | BindingFlags.NonPublic));
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (string name in MainMethodsToSkip)
            {
                int n = 0;
                foreach (MethodInfo m in typeof(Main).GetMethods(All))
                {
                    if (m.Name != name) continue;
                    try { _h.Patch(m, prefix: skip); n++; }
                    catch (Exception ex) { _log.Error("Patch failed for Main." + name + ": " + ex.Message); }
                }
                _log.Info("Skip-patch Main." + name + ": " + n + " method(s)");
            }

            // Sky: IHorizonRenderer implementations (interface found by name in the Terraria assembly).
            try
            {
                Type iface = null;
                foreach (Type t in typeof(Main).Assembly.GetTypes()) if (t.IsInterface && t.Name == "IHorizonRenderer") { iface = t; break; }
                int hz = 0;
                if (iface == null) _log.Warn("IHorizonRenderer interface not found; sky may remain.");
                else foreach (Type t in typeof(Main).Assembly.GetTypes())
                {
                    if (t.IsInterface || t.IsAbstract || !iface.IsAssignableFrom(t)) continue;
                    foreach (MethodInfo m in t.GetMethods(All))
                        if (m.Name == "DrawHorizon" || m.Name == "DrawSun" || m.Name == "DrawLensFlare")
                        { try { _h.Patch(m, prefix: skip); hz++; } catch (Exception ex) { _log.Error("Horizon patch failed " + t.Name + "." + m.Name + ": " + ex.Message); } }
                }
                _log.Info("Skip-patch IHorizonRenderer implementations: " + hz + " method(s)");
            }
            catch (Exception ex) { _log.Error("Horizon patching failed: " + ex.Message); }

            // Scene layer render targets (1.4.5). DoDraw blits cached WorldSceneLayerTarget textures inline (8 call sites in the
            // dump: sky, walls, tiles, water). They are only re-rendered when the camera leaves their margin, and the camera is
            // locked, so the pre-hide sky stayed on screen no matter what we skipped. Fix: while Active, never render into them
            // and clear them to transparent instead, so every blit draws nothing.
            try
            {
                Type slt = null;
                foreach (Type t in typeof(Main).Assembly.GetTypes()) if (t.Name == "WorldSceneLayerTarget") { slt = t; break; }
                if (slt == null) _log.Warn("WorldSceneLayerTarget type not found; cached sky/tile layers may remain on screen.");
                else
                {
                    _sceneTexProp = slt.GetProperty("Texture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var scenePrefix = new HarmonyMethod(typeof(WorldHider).GetMethod("SceneTargetPrefix", BindingFlags.Static | BindingFlags.NonPublic));
                    int sc = 0;
                    foreach (MethodInfo m in slt.GetMethods(All))
                        if (m.Name == "UpdateContent")
                        { try { _h.Patch(m, prefix: scenePrefix); sc++; } catch (Exception ex) { _log.Error("Scene-target patch failed for " + m.Name + ": " + ex.Message); } }
                    _log.Info("Scene-target patch WorldSceneLayerTarget.UpdateContent: " + sc + " method(s); Texture property = "
                        + (_sceneTexProp == null ? "NOT FOUND" : _sceneTexProp.PropertyType.FullName));
                    // Members, so the next iteration can be fixed from facts if this isn't enough.
                    var sb = new System.Text.StringBuilder("WorldSceneLayerTarget members:");
                    foreach (FieldInfo f in slt.GetFields(All)) sb.Append(" [F ").Append(f.FieldType.Name).Append(' ').Append(f.Name).Append(']');
                    foreach (PropertyInfo pi in slt.GetProperties(All)) sb.Append(" [P ").Append(pi.PropertyType.Name).Append(' ').Append(pi.Name).Append(']');
                    foreach (MethodInfo mi in slt.GetMethods(All)) if (!mi.IsSpecialName) sb.Append(" [M ").Append(mi.Name).Append(']');
                    _log.Info(sb.ToString());
                }
            }
            catch (Exception ex) { _log.Error("Scene-target patching failed: " + ex.Message); }

            // Camera lock (screen pixel = world pixel + fixed origin).
            var cam = new HarmonyMethod(typeof(WorldHider).GetMethod("CameraPrefix", BindingFlags.Static | BindingFlags.NonPublic));
            int cm = 0;
            foreach (MethodInfo m in typeof(Main).GetMethods(All))
                if (m.Name == "DoDraw_UpdateCameraPosition") { try { _h.Patch(m, prefix: cam); cm++; } catch (Exception ex) { _log.Error("Camera patch failed: " + ex.Message); } }
            _log.Info("Camera-lock patch: " + cm + " method(s)");

            // DRAW GATE: from the end of RenderToTargets until the entity pass starts, drop every SpriteBatch.Draw (and the
            // non-generic GraphicsDevice primitive draws). Player/NPC helper targets are prepared BEFORE RenderToTargets, so they are
            // unaffected. The gate is reset at the start and end of every DoDraw so a stray frame can never leave drawing off.
            try
            {
                var gatePrefix = new HarmonyMethod(typeof(WorldHider).GetMethod("GatePrefix", BindingFlags.Static | BindingFlags.NonPublic));
                const BindingFlags Pub = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                int g1 = 0, g2 = 0;
                foreach (MethodInfo m in typeof(SpriteBatch).GetMethods(Pub))
                {
                    if (m.Name != "Draw") continue;
                    try { _h.Patch(m, prefix: gatePrefix); g1++; }
                    catch (Exception ex) { _log.Warn("Gate patch failed for SpriteBatch.Draw(" + m.GetParameters().Length + " args): " + ex.Message); }
                }
                foreach (MethodInfo m in typeof(GraphicsDevice).GetMethods(Pub))
                {
                    if (m.IsGenericMethodDefinition) continue;
                    if (m.Name != "DrawPrimitives" && m.Name != "DrawIndexedPrimitives") continue;
                    try { _h.Patch(m, prefix: gatePrefix); g2++; }
                    catch (Exception ex) { _log.Warn("Gate patch failed for GraphicsDevice." + m.Name + ": " + ex.Message); }
                }
                _log.Info("Draw-gate patches: SpriteBatch.Draw " + g1 + " overload(s), GraphicsDevice primitives " + g2);

                var gateOn = new HarmonyMethod(typeof(WorldHider).GetMethod("GateOnPostfix", BindingFlags.Static | BindingFlags.NonPublic));
                var gateOffPre = new HarmonyMethod(typeof(WorldHider).GetMethod("GateOffPrefix", BindingFlags.Static | BindingFlags.NonPublic));
                var gateOffPost = new HarmonyMethod(typeof(WorldHider).GetMethod("GateOffPostfix", BindingFlags.Static | BindingFlags.NonPublic));
                int onN = 0, offN = 0;
                foreach (MethodInfo m in typeof(Main).GetMethods(All))
                {
                    if (m.Name == "RenderToTargets" || m.Name == "SetBackColor") { try { _h.Patch(m, postfix: gateOn); onN++; } catch (Exception ex) { _log.Error("Gate-on patch failed for " + m.Name + ": " + ex.Message); } }
                    else if (m.Name == "DoDraw") { try { _h.Patch(m, prefix: gateOffPre, postfix: gateOffPost); offN++; } catch (Exception ex) { _log.Error("Gate-off patch failed: " + ex.Message); } }
                }
                _log.Info("Draw-gate switches: on-points " + onN + ", DoDraw reset " + offN);

                _screenTargets.Clear();
                var names = new System.Text.StringBuilder("Main static RenderTarget2D fields:");
                foreach (FieldInfo f in typeof(Main).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (f.FieldType != typeof(RenderTarget2D)) continue;
                    names.Append(' ').Append(f.Name);
                    if (f.Name.StartsWith("screenTarget")) _screenTargets.Add(f);
                }
                _log.Info(names.ToString());
                _log.Info("Erase will also clear " + _screenTargets.Count + " screenTarget* field(s) and the backbuffer.");
            }
            catch (Exception ex) { _log.Error("Draw-gate setup failed: " + ex.Message); }

            // ERASE PASS: whatever drew the sky (we could not identify it), wipe the whole frame to transparent right before
            // Terraria draws the world's NPCs/players/projectiles. GraphicsDevice.Clear is not affected by pending sprite batches
            // and works on whichever render target is bound. Everything drawn before this point disappears; entities stay.
            var erase = new HarmonyMethod(typeof(WorldHider).GetMethod("ErasePrefix", BindingFlags.Static | BindingFlags.NonPublic));
            int er = 0;
            foreach (MethodInfo m in typeof(Main).GetMethods(All))
                if (m.Name == "DoDraw_WallsTilesNPCs") { try { _h.Patch(m, prefix: erase); er++; } catch (Exception ex) { _log.Error("Erase-pass patch failed: " + ex.Message); } }
            _log.Info("Erase-pass patch Main.DoDraw_WallsTilesNPCs: " + er + " method(s)");

            // v0.4.4 BLACK SKY AT THE SOURCE. The window is see-through where pixels are pure black (verified in the spike), so any
            // sky colour that still reaches the screen is a leak. SetBackColor computes every sky/background tint each frame; force them
            // all to black afterwards so even a draw call we failed to skip paints black = transparent.
            try
            {
                var skyPost = new HarmonyMethod(typeof(WorldHider).GetMethod("BlackSkyPostfix", BindingFlags.Static | BindingFlags.NonPublic));
                int sk = 0;
                foreach (MethodInfo m in typeof(Main).GetMethods(All))
                    if (m.Name == "SetBackColor") { try { _h.Patch(m, postfix: skyPost); sk++; } catch (Exception ex) { _log.Error("Black-sky patch failed: " + ex.Message); } }
                _skyFields.Clear();
                foreach (string fname in new string[0])
                {
                    FieldInfo f = typeof(Main).GetField(fname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null && f.FieldType == typeof(Color)) _skyFields.Add(f);
                }
                _log.Info("Black-sky patch Main.SetBackColor: " + sk + " method(s); colour fields forced to black: " + _skyFields.Count);
            }
            catch (Exception ex) { _log.Error("Black-sky setup failed: " + ex.Message); }

            // v0.4.4 EndCapture pre-clear. FilterManager.EndCapture composites the captured world target onto the backbuffer. Wipe the
            // backbuffer first so nothing drawn into it earlier in the frame (before the capture started) can sit underneath.
            try
            {
                var capPre = new HarmonyMethod(typeof(WorldHider).GetMethod("EndCapturePrefix", BindingFlags.Static | BindingFlags.NonPublic));
                int ec = 0;
                Type fm = null;
                foreach (Type t in typeof(Main).Assembly.GetTypes()) if (t.Name == "FilterManager") { fm = t; break; }
                if (fm != null)
                    foreach (MethodInfo m in fm.GetMethods(All))
                        if (m.Name == "EndCapture") { try { _h.Patch(m, prefix: capPre); ec++; } catch (Exception ex) { _log.Error("EndCapture patch failed: " + ex.Message); } }
                _log.Info("EndCapture pre-clear patch: " + ec + " method(s)" + (fm == null ? "  (FilterManager type not found)" : ""));
            }
            catch (Exception ex) { _log.Error("EndCapture setup failed: " + ex.Message); }

            // v0.4.5 FULL-BRIGHT ENTITIES. Mirrored window tiles are solid, so they shadow whatever stands under a title bar, and the
            // character/NPCs are tinted by Lighting.GetColor. Tiles are never drawn here, so lighting only matters for entities:
            // return the unlit colour for every GetColor lookup while desktop mode is active.
            try
            {
                Type lt = null;
                foreach (Type t in typeof(Main).Assembly.GetTypes()) if (t.Name == "Lighting" && t.Namespace == "Terraria") { lt = t; break; }
                int lc = 0;
                if (lt == null) _log.Warn("Terraria.Lighting type not found; characters may look dark.");
                else
                {
                    var lightPost = new HarmonyMethod(typeof(WorldHider).GetMethod("LightPostfix", BindingFlags.Static | BindingFlags.NonPublic));
                    foreach (MethodInfo m in lt.GetMethods(All))
                    {
                        if (m.Name != "GetColor" || m.ReturnType != typeof(Color) || m.IsGenericMethodDefinition) continue;
                        try { _h.Patch(m, postfix: lightPost); lc++; }
                        catch (Exception ex) { _log.Warn("Light patch failed for GetColor(" + m.GetParameters().Length + " args): " + ex.Message); }
                    }
                }
                _log.Info("Full-bright patch Lighting.GetColor: " + lc + " overload(s)");
            }
            catch (Exception ex) { _log.Error("Full-bright setup failed: " + ex.Message); }

            // v0.4.6 EASY-BREAK WINDOW TILES: any pickaxe hit on a window tile breaks it at once (fail=false) and drops nothing.
            try
            {
                var killPre = new HarmonyMethod(typeof(WorldHider).GetMethod("KillTilePrefix", BindingFlags.Static | BindingFlags.NonPublic));
                int kt = 0;
                foreach (MethodInfo m in typeof(WorldGen).GetMethods(All))
                    if (m.Name == "KillTile" && m.GetParameters().Length == 5) { try { _h.Patch(m, prefix: killPre); kt++; } catch (Exception ex) { _log.Error("KillTile patch failed: " + ex.Message); } }
                _log.Info("Easy-break patch WorldGen.KillTile: " + kt + " method(s)");
            }
            catch (Exception ex) { _log.Error("Easy-break setup failed: " + ex.Message); }

            // Save guard: after we wipe tiles, the world must NEVER be written to disk.
            try
            {
                var guard = new HarmonyMethod(typeof(WorldHider).GetMethod("SaveGuardPrefix", BindingFlags.Static | BindingFlags.NonPublic));
                Type wf = typeof(Main).Assembly.GetType("Terraria.IO.WorldFile");
                int sg = 0;
                if (wf != null)
                    foreach (MethodInfo m in wf.GetMethods(All))
                        if (m.Name == "SaveWorld" || m.Name == "SaveWorld_Version2" || m.Name == "SaveWorldTiles")
                        { try { _h.Patch(m, prefix: guard); sg++; } catch (Exception ex) { _log.Error("Save guard patch failed " + m.Name + ": " + ex.Message); } }
                SaveGuardInstalled = sg > 0;
                _log.Info("Save-guard patch on WorldFile.SaveWorld*: " + sg + " method(s)" + (sg == 0 ? "  <-- NOT INSTALLED, desktop mode will refuse to start" : ""));
            }
            catch (Exception ex) { _log.Error("Save guard failed: " + ex.Message); }

            // Opaque-black clears -> transparent. Only the (Color) and (ClearOptions, Color, float, int) overloads.
            var clearPrefix = new HarmonyMethod(typeof(WorldHider).GetMethod("ClearPrefix", BindingFlags.Static | BindingFlags.NonPublic));
            int c = 0;
            foreach (MethodInfo m in typeof(GraphicsDevice).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "Clear") continue;
                ParameterInfo[] ps = m.GetParameters();
                bool hasColorParam = false;
                foreach (ParameterInfo p in ps) if (p.ParameterType == typeof(Color) && p.Name == "color") hasColorParam = true;
                if (!hasColorParam) continue;
                // The 4-arg XNA overload is a mixed-mode method with custom modifiers; Harmony throws
                // "Unable to cast 'ModifierType' to 'System.Type'" on it (seen in the log). Clear(Color) is the one that matters
                // and patches fine, so leave the others alone.
                if (ps.Length != 1) { _log.Info("Clear-patch: leaving Clear(" + ps.Length + " args) unpatched (not patchable by Harmony)."); continue; }
                try { _h.Patch(m, prefix: clearPrefix); c++; }
                catch (Exception ex) { _log.Error("Patch failed for GraphicsDevice.Clear(" + ps.Length + " args): " + ex.Message); }
            }
            _log.Info("Clear-patch GraphicsDevice.Clear: " + c + " overload(s)");
        }

        // Patches stay installed for the process lifetime on purpose: the save guard must outlive the mod,
        // and every other patch is inert while Active == false.
        public static void Uninstall() { Active = false; CameraLock = false; Gate = false; }

        private static bool SaveGuardPrefix() { return !Wiped; }

        private static bool CameraPrefix()
        {
            if (!(Active && CameraLock)) return true;
            Main.screenPosition = CameraPx;
            return false;
        }

        public static volatile bool Gate;
        private static int _gateBlocked;
        private static readonly System.Collections.Generic.List<FieldInfo> _screenTargets = new System.Collections.Generic.List<FieldInfo>();
        private static bool _eraseLogged, _eraseFailed;

        private static bool GatePrefix() { if (!Gate) return true; _gateBlocked++; return false; }
        private static void GateOnPostfix() { if (Active && !Main.gameMenu) Gate = true; }
        private static void GateOffPrefix() { Gate = false; }
        private static void GateOffPostfix() { Gate = false; }

        private static void ErasePrefix()
        {
            int blocked = _gateBlocked; _gateBlocked = 0;
            Gate = false;
            if (!Active) return;
            GraphicsDevice gd = Main.instance == null ? null : Main.instance.GraphicsDevice;
            if (gd == null) return;
            RenderTargetBinding[] bound = null;
            try
            {
                bound = gd.GetRenderTargets();
                if (!_eraseLogged)
                {
                    var sb = new System.Text.StringBuilder("Erase pass #1: bound render targets = " + (bound == null ? 0 : bound.Length));
                    if (bound != null) foreach (RenderTargetBinding b in bound) { var t2 = b.RenderTarget as Texture2D; sb.Append(t2 == null ? " [?]" : " [" + t2.Width + "x" + t2.Height + "]"); }
                    sb.Append("; gate dropped " + blocked + " draw calls before world entities");
                    _log.Info(sb.ToString());
                    _eraseLogged = true;
                }
                // 1) backbuffer
                gd.SetRenderTarget(null);
                gd.Clear(Color.Transparent);
                // 2) full-screen scratch targets
                for (int i = 0; i < _screenTargets.Count; i++)
                {
                    RenderTarget2D rt = _screenTargets[i].GetValue(null) as RenderTarget2D;
                    if (rt == null || rt.IsDisposed) continue;
                    gd.SetRenderTarget(rt);
                    gd.Clear(Color.Transparent);
                }
            }
            catch (Exception ex)
            {
                if (!_eraseFailed) { _eraseFailed = true; _log.Error("Erase pass failed: " + ex.Message); }
            }
            finally
            {
                try
                {
                    // 3) restore whatever was bound, and wipe it too
                    if (bound != null && bound.Length > 0) gd.SetRenderTargets(bound); else gd.SetRenderTarget(null);
                    gd.Clear(Color.Transparent);
                }
                catch (Exception ex2) { if (!_eraseFailed) { _eraseFailed = true; _log.Error("Erase pass restore failed: " + ex2.Message); } }
            }
        }

        private static PropertyInfo _sceneTexProp;
        private static int _sceneCalls, _sceneCleared;
        private static bool _sceneWarned;

        // Replaces WorldSceneLayerTarget.UpdateContent(Action render) while Active: no world rendering, target wiped to transparent.
        private static bool SceneTargetPrefix(object __instance)
        {
            if (!Active) return true;
            try
            {
                _sceneCalls++;
                RenderTarget2D rt = _sceneTexProp == null ? null : _sceneTexProp.GetValue(__instance, null) as RenderTarget2D;
                GraphicsDevice gd = Main.instance == null ? null : Main.instance.GraphicsDevice;
                if (rt != null && !rt.IsDisposed && gd != null)
                {
                    gd.SetRenderTarget(rt);
                    gd.Clear(Color.Transparent);
                    gd.SetRenderTarget(null);
                    _sceneCleared++;
                }
                else if (!_sceneWarned)
                {
                    _sceneWarned = true;
                    _log.Warn("Scene target not clearable (Texture null or not a RenderTarget2D). Instance " + __instance.GetType().Name);
                }
                if (_sceneCalls == 600) _log.Info("Scene targets: " + _sceneCleared + " of " + _sceneCalls + " UpdateContent calls cleared to transparent.");
            }
            catch (Exception ex)
            {
                if (!_sceneWarned) { _sceneWarned = true; _log.Error("SceneTargetPrefix failed: " + ex); }
            }
            return false;
        }

        private static readonly System.Collections.Generic.List<FieldInfo> _skyFields = new System.Collections.Generic.List<FieldInfo>();
        private static bool _endCapLogged, _endCapFailed;

        private static void BlackSkyPostfix(ref Color sunColor, ref Color moonColor)
        {
            if (!Active) return;
            try
            {
                // v0.4.5: do NOT touch ColorOfTheSkies/bgColor/backColor. Terraria derives ambient light from them, so blackening them
                // blackened the character too (black = see-through). Only the sun/moon tints are safe to zero.
                sunColor = Color.Black; moonColor = Color.Black;
            }
            catch { }
        }

        private static void EndCapturePrefix()
        {
            if (!Active || Main.gameMenu) return;
            try
            {
                GraphicsDevice gd = Main.instance == null ? null : Main.instance.GraphicsDevice;
                if (gd == null) return;
                gd.SetRenderTarget(null);
                gd.Clear(Color.Transparent);
                if (!_endCapLogged) { _endCapLogged = true; _log.Info("EndCapture pre-clear ran (backbuffer wiped before world composite)."); }
            }
            catch (Exception ex) { if (!_endCapFailed) { _endCapFailed = true; _log.Error("EndCapture pre-clear failed: " + ex.Message); } }
        }

        // Harmony injects __args (all arguments) and ref __result. GetColor(x, y) -> white; GetColor(x, y, Color c) -> c unchanged.
        private static void LightPostfix(object[] __args, ref Color __result)
        {
            if (!Active) return;
            try
            {
                Color c = Color.White;
                if (__args != null) for (int i = 0; i < __args.Length; i++) if (__args[i] is Color) { c = (Color)__args[i]; break; }
                __result = c;
            }
            catch { }
        }

        public static Func<int, int, bool> IsWindowTile;

        // KillTile(int i, int j, bool fail, bool effectOnly, bool noItem): args 2 and 4 are rewritten through __args.
        private static void KillTilePrefix(object[] __args)
        {
            if (!Active || IsWindowTile == null || __args == null || __args.Length < 5) return;
            try
            {
                if (!(__args[0] is int) || !(__args[1] is int)) return;
                if (!IsWindowTile((int)__args[0], (int)__args[1])) return;
                if (!(bool)__args[3]) { __args[2] = false; __args[4] = true; }   // effectOnly stays as-is; otherwise: break fully, no drop
            }
            catch { }
        }

        private static bool SkipPrefix() { return !Active; }   // false => original method is skipped

        private static void ClearPrefix(ref Color color)
        {
            if (Active && color.R == 0 && color.G == 0 && color.B == 0 && color.A == 255) color = Color.Transparent;
        }
    }
}
