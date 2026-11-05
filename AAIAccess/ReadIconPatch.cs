extern alias GK1;
using HarmonyLib;
using Il2CppGK_Common.UI.SousaPart;

namespace AAIAccess
{
    /// <summary>The game sets the Examine / Talk prompt's check mark: tell the navigator.</summary>
    [HarmonyPatch(typeof(SousaPlayerActionButton), nameof(SousaPlayerActionButton.SetReadIcon))]
    internal static class Patch_SetReadIcon
    {
        private static void Postfix(bool isRead)
        {
            try { Navigator.OnReadIcon(isRead); } catch { }
        }
    }
}

namespace AAIAccess
{
    /// <summary>The game shows its "Deduce" button: a contradiction can be pointed out here.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Il2CppGK_Common.UI.Sousa.GKSuiriButton), nameof(Il2CppGK_Common.UI.Sousa.GKSuiriButton.Opne))]
    internal static class Patch_DeduceOpen
    {
        private static void Postfix() { try { ExamineScreen.OnDeduceButton(true); } catch { } }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2CppGK_Common.UI.Sousa.GKSuiriButton), nameof(Il2CppGK_Common.UI.Sousa.GKSuiriButton.Close))]
    internal static class Patch_DeduceClose
    {
        private static void Postfix() { try { ExamineScreen.OnDeduceButton(false); } catch { } }
    }
}

namespace AAIAccess
{
    /// <summary>Evidence "Check" pages (first game): hooks on the game's own script steps for showing them.
    /// Applied by hand, so a missing method can't stop the other patches.</summary>
    internal static class DetailPatches
    {
        public static void Apply(HarmonyLib.Harmony h)
        {
            Hook(h, "Func_DetailView_2D_Load", nameof(Load));
            Hook(h, "Func_DetailView_2D_End", nameof(End));
            Hook(h, "Func_DetailView_ReturnSingleView", nameof(Ret));
            Hook(h, "Func_DetailView_2D_Change", nameof(Change));
            Hook(h, "Func_DetailView_2D_Scroll", nameof(Scroll));
        }

        private static void Hook(HarmonyLib.Harmony h, string method, string post)
        {
            try
            {
                var m = HarmonyLib.AccessTools.Method(typeof(GK1::Il2Cpp.ItemListMenu), method);
                if (m == null) { Main.Log.Warning("No method " + method); return; }
                h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(DetailPatches), post));
            }
            catch (System.Exception e) { Main.Log.Warning($"Hook {method} failed: {e.Message}"); }
        }

        private static void Load() { try { EvidenceDetail.OnDetail2D("load", true); } catch { } }
        private static void End() { try { EvidenceDetail.OnDetail2D("end", false); } catch { } }
        private static void Ret() { try { EvidenceDetail.OnDetail2D("return", false); } catch { } }
        private static float _lastScrollLog;
        private static float _lastChangeLog;
        private static void Change() { try { if (UnityEngine.Time.unscaledTime - _lastChangeLog > 2f) { _lastChangeLog = UnityEngine.Time.unscaledTime; Main.Log.Msg("Detail view page change"); } } catch { } }
        private static void Scroll() { try { if (UnityEngine.Time.unscaledTime - _lastScrollLog > 2f) { _lastScrollLog = UnityEngine.Time.unscaledTime; Main.Log.Msg("Detail view scroll"); } } catch { } }
    }
}

namespace AAIAccess
{
    /// <summary>The title screen's background is chosen from the latest save (game, episode, section). The demo only
    /// has the backgrounds of its own chapters, so a save from further in the full game asks for one it doesn't have
    /// and the title screen never finishes loading. Swap a missing background for one the game has.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Il2CppGK_System.MasterData.TitleVisualMasterData), nameof(Il2CppGK_System.MasterData.TitleVisualMasterData.FindDate))]
    internal static class Patch_TitleVisual
    {
        private const string Fmt = "Assets/GK_Common/Prefabs/Title/{0}.prefab";   // the game's names are like Title_BGAnimation_R_6

        private static bool Has(string bg)
        {
            try { return Il2CppGK_System.Resource.ResourceManager.AddressIsExist(string.Format(Fmt, bg)); } catch { return true; }
        }

        private static void Postfix(Il2CppGK_System.MasterData.TitleVisualMasterData.TitleVisual __result)
        {
            try
            {
                if (__result == null) return;
                string bg = __result.m_titlevisual_bg;
                if (string.IsNullOrEmpty(bg) || Has(bg)) return;
                if (!bg.StartsWith("Title_BGAnimation")) { Main.Log.Warning($"Title background {bg}: unexpected name, left as it is"); return; }
                foreach (var alt in new[] { "Title_BGAnimation_R_0", "Title_BGAnimation_R_1", "Title_BGAnimation_R_11", "Title_BGAnimation_R_12", "Title_BGAnimation_R" })
                    if (Has(alt)) { __result.m_titlevisual_bg = alt; Main.Log.Msg($"Title background {bg} isn't in this version of the game; using {alt}"); return; }
                Main.Log.Warning($"Title background {bg} isn't in this version of the game, and no other was found");
            }
            catch (System.Exception e) { Main.Log.Warning("Title background check: " + e.Message); }
        }
    }
}
