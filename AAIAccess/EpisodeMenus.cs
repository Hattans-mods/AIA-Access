using System;
using System.Text;
using HarmonyLib;
using Il2CppGK_Common.UI;
using Il2CppTMPro;
using UnityEngine;

namespace AAIAccess
{
    internal static class EpisodeMenus
    {
        /// <summary>All visible texts under an object, joined (e.g. "Episode 1, Turnabout Visitor").</summary>
        public static string TextsUnder(GameObject obj)
        {
            if (obj == null) return "";
            var parts = new System.Collections.Generic.List<string>();
            foreach (var t in obj.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!t.isActiveAndEnabled) continue;
                string s = TextCleaner.Clean(t.text);
                if (s.Length > 0 && !parts.Contains(s)) parts.Add(s);
            }
            return string.Join(", ", parts);
        }

        private static float _nextScan;
        private static ChapterSelect _chapter;
        private static EpisodeSelect _episodes;
        private static int _lastEpisode = -99, _lastLoggedNo = -99;
        private static float _nextEpisodeScan;
        private static string _pendingButton;
        private static float _pendingAt;

        public static bool EpisodeScreenActive =>
            _episodes != null && !_episodes.WasCollected && _episodes.isActiveAndEnabled && _episodes.IsActive;

        /// <summary>Button focus on the episode screen is held briefly so the episode can be read before it.</summary>
        public static void DeferButton(string text) { _pendingButton = text; _pendingAt = Time.unscaledTime + 0.15f; }

        private static void FlushPending()
        {
            if (_pendingButton != null && Time.unscaledTime >= _pendingAt) { Speech.Say(_pendingButton, true, false); _pendingButton = null; }
        }

        /// <summary>Episode carousel: announce the episode number and title when the selection changes.</summary>
        private static void PollEpisodes()
        {
            if (_episodes == null || _episodes.WasCollected)
            {
                if (Time.unscaledTime < _nextEpisodeScan) return;
                _nextEpisodeScan = Time.unscaledTime + 1f;
                _episodes = UnityEngine.Object.FindObjectOfType<EpisodeSelect>();
                if (_episodes == null) return;
            }
            if (!_episodes.isActiveAndEnabled || !_episodes.IsActive) { _lastEpisode = -99; if (_pendingButton != null) { Speech.Say(_pendingButton, true, false); _pendingButton = null; } return; }
            int no = _episodes.SelectedEpisodeNo;
            if (no != _lastEpisode)
            {
                string text = EpisodeText(no);
                if (no != _lastLoggedNo) { _lastLoggedNo = no; Main.Log.Msg($"Episode selected {no}: {text}"); }
                if (string.IsNullOrEmpty(text)) { FlushPending(); return; }   // not set up yet (e.g. "episode 0" while opening)
                _lastEpisode = no;
                // Episode first, then the highlighted button.
                Speech.Say(Menus.Join(text, _pendingButton), true, false);
                _pendingButton = null;
                return;
            }
            FlushPending();
        }

        private static string EpisodeText(int no)
        {
            foreach (var arr in new[] { _episodes.items_eng, _episodes.items, _episodes.episodeFrames })
            {
                if (arr == null) continue;
                // The selected number counts from 0 (0 = episode 1).
                foreach (int i in new[] { no })
                {
                    if (i < 0 || i >= arr.Length) continue;
                    try
                    {
                        var it = arr[i];
                        string num = it.number != null && it.number.gameObject.activeInHierarchy ? TextCleaner.Clean(it.number.text) : "";
                        string title = it.title != null && it.title.gameObject.activeInHierarchy ? TextCleaner.Clean(it.title.text) : "";
                        if (title.Length > 0 && (num.Length == 0 || num.Contains((no + 1).ToString()))) return Menus.Join(num, title);
                        if (title.Length == 0 && num.Contains((no + 1).ToString())) return num;
                    }
                    catch { }
                }
            }
            return "";
        }
        private static int _lastChapterIdx = -1;

        /// <summary>Chapter list inside an episode: read the highlighted chapter.</summary>
        public static void Poll()
        {
            try
            {
                float now = Time.unscaledTime;
                try { PollEpisodes(); } catch (Exception ex) { Main.Log.Error("Episode poll failed: " + ex.Message); _episodes = null; }
                if ((_chapter == null || _chapter.WasCollected) && now >= _nextScan)
                {
                    _nextScan = now + 1f;
                    _chapter = UnityEngine.Object.FindObjectOfType<ChapterSelect>();
                }
                if (_chapter == null || _chapter.WasCollected || !_chapter.isActiveAndEnabled || !_chapter.gameObject.activeInHierarchy) { _lastChapterIdx = -1; return; }
                var list = _chapter.chapterList;
                int idx = _chapter.buttonIndex;
                if (list == null || idx < 0 || idx >= list.Count || idx == _lastChapterIdx) return;
                _lastChapterIdx = idx;
                string text = TextsUnder(list[idx]);
                Main.Log.Msg($"Chapter {idx}: {text}");  // chapter buttons already announce themselves
            }
            catch (Exception e) { Main.Log.Error("Chapter poll failed: " + e.Message); _chapter = null; _nextScan = Time.unscaledTime + 2f; }
        }
    }

    // The episode carousel calls OnCenter with the episode that just moved to the middle.
    [HarmonyPatch(typeof(EpisodeSelect), "OnCenter")]
    internal static class Patch_EpisodeOnCenter
    {
        private static string _last = "";
        private static float _lastAt;
        private static void Postfix(GameObject obj)
        {
            try
            {
                string text = EpisodeMenus.TextsUnder(obj);
                Main.Log.Msg($"Episode centered: {obj?.name}: {text}");
                if (text.Length == 0) return;
                float now = Time.unscaledTime;
                if (text == _last && now - _lastAt < 1f) return;
                _last = text; _lastAt = now;
                Speech.Say(text, true, false);
            }
            catch (Exception e) { Main.Log.Error("Episode center failed: " + e.Message); }
        }
    }
}
