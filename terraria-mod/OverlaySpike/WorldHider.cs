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

            // Camera lock (screen pixel = world pixel + fixed origin).
            var cam = new HarmonyMethod(typeof(WorldHider).GetMethod("CameraPrefix", BindingFlags.Static | BindingFlags.NonPublic));
            int cm = 0;
            foreach (MethodInfo m in typeof(Main).GetMethods(All))
                if (m.Name == "DoDraw_UpdateCameraPosition") { try { _h.Patch(m, prefix: cam); cm++; } catch (Exception ex) { _log.Error("Camera patch failed: " + ex.Message); } }
            _log.Info("Camera-lock patch: " + cm + " method(s)");

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
                try { _h.Patch(m, prefix: clearPrefix); c++; }
                catch (Exception ex) { _log.Error("Patch failed for GraphicsDevice.Clear(" + ps.Length + " args): " + ex.Message); }
            }
            _log.Info("Clear-patch GraphicsDevice.Clear: " + c + " overload(s)");
        }

        // Patches stay installed for the process lifetime on purpose: the save guard must outlive the mod,
        // and every other patch is inert while Active == false.
        public static void Uninstall() { Active = false; CameraLock = false; }

        private static bool SaveGuardPrefix() { return !Wiped; }

        private static bool CameraPrefix()
        {
            if (!(Active && CameraLock)) return true;
            Main.screenPosition = CameraPx;
            return false;
        }

        private static bool SkipPrefix() { return !Active; }   // false => original method is skipped

        private static void ClearPrefix(ref Color color)
        {
            if (Active && color.R == 0 && color.G == 0 && color.B == 0 && color.A == 255) color = Color.Transparent;
        }
    }
}
