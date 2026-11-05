using System;
using HarmonyLib;
using Il2CppGK_Common.UI.Story;
using Il2CppGK_Common.UI.Sousa;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>Full-screen text cards: episode/chapter titles, location/time cards, "next episode" cards.</summary>
    internal static class TextCards
    {
        private static string _last = "";
        private static float _lastAt;
        private static GameObject _pendingObj;
        private static float _pendingAt;

        public static void Say(string text, string source)
        {
            text = TextCleaner.Clean(text);
            if (string.IsNullOrEmpty(text)) return;
            float now = Time.unscaledTime;
            if (text == _last && now - _lastAt < 3f) return;
            _last = text; _lastAt = now;
            Main.Log.Msg($"Text card ({source}): {text}");
            Speech.Say(text, interrupt: false);
        }

        /// <summary>Read all texts under a card a moment after it is shown (its text may be filled in late).</summary>
        public static void ReadLater(GameObject obj) { _pendingObj = obj; _pendingAt = Time.unscaledTime + 0.3f; }

        private static float _scanAt;
        private static readonly System.Collections.Generic.HashSet<IntPtr> _shown = new System.Collections.Generic.HashSet<IntPtr>();

        /// <summary>Notice "next episode" and chapter title cards when they appear (instead of patching their Show).</summary>
        private static void Scan()
        {
            float now = Time.unscaledTime;
            if (now < _scanAt) return;
            _scanAt = now + 0.25f;
            var seen = new System.Collections.Generic.HashSet<IntPtr>();
            try
            {
                foreach (var c in UnityEngine.Object.FindObjectsOfType<GKEpisodeNextTextCut>())
                    if (c != null && c.gameObject.activeInHierarchy) { seen.Add(c.Pointer); if (!_shown.Contains(c.Pointer)) ReadLater(c.gameObject); }
                foreach (var c in UnityEngine.Object.FindObjectsOfType<GKSubTitleTextCut>())
                    if (c != null && c.gameObject.activeInHierarchy) { seen.Add(c.Pointer); if (!_shown.Contains(c.Pointer)) ReadLater(c.gameObject); }
            }
            catch { }
            _shown.Clear();
            foreach (var p in seen) _shown.Add(p);
        }

        public static void Poll()
        {
            Scan();
            if (_pendingObj == null || Time.unscaledTime < _pendingAt) return;
            try { Say(EpisodeMenus.TextsUnder(_pendingObj), "card"); } catch { }
            _pendingObj = null;
        }
    }

    [HarmonyPatch(typeof(GKSubTitleTextCut), nameof(GKSubTitleTextCut.SetTitle))]
    internal static class Patch_SubTitle
    {
        private static void Postfix(string title, string no) { try { TextCards.Say(Menus.Join(TextCleaner.Clean(no), TextCleaner.Clean(title)), "title"); } catch { } }
    }

    [HarmonyPatch(typeof(GKSousaTextCut), nameof(GKSousaTextCut.Setup))]
    internal static class Patch_SousaTextCut
    {
        private static void Postfix(string text) { try { TextCards.Say(text, "location"); } catch { } }
    }

    // Note: GKEpisodeNextTextCut.Show and GKSubTitleTextCut.Show must NOT be patched. The game's build shares one
    // piece of machine code between them and GKSousaTextCut.Show (and two more), so a patch on one catches all of
    // them and loops forever when an evidence text page is shown (stack overflow crash). We watch for the cards instead.
}
