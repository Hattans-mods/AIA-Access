using System;
using Il2CppGK_Common.UI;
using Il2CppGK_Common.UI.Option;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AAIAccess
{
    /// <summary>Options screen: reads the focused setting row and its value inside a category.</summary>
    internal static class OptionsMenu
    {
        private static float _nextScan;
        private static GKOption[] _options = new GKOption[0];
        private static string _lastRowKey = "";
        private static string _lastValue = "";
        private static Il2CppGK_Common.UI.Option.Category.CategoryMenuState _lastState;
        private static string _lastErr;

        public static void Poll()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now >= _nextScan)
                {
                    _nextScan = now + 1f;
                    var f = UnityEngine.Object.FindObjectsOfType<GKOption>();
                    _options = new GKOption[f.Length];
                    for (int i = 0; i < f.Length; i++) _options[i] = f[i];
                }
                foreach (var o in _options)
                {
                    if (o == null || o.WasCollected || !o.isActiveAndEnabled) continue;
                    int cat = o.m_SelectCategoryIndex;
                    var menus = o.m_ViewMenu; var objs = o.m_ViewObjects;
                    if (menus == null || objs == null || cat < 0 || cat >= menus.Count || cat >= objs.Count) continue;
                    var menu = menus[cat];
                    if (menu == null || objs[cat] == null) continue;
                    var st = menu.State;
                    if (st != _lastState) { _lastState = st; Main.Log.Msg($"Options category {cat} state {st}"); }
                    // Only read rows once the player has entered the category (not while browsing categories).
                    if (st != Il2CppGK_Common.UI.Option.Category.CategoryMenuState.Open) { _lastRowKey = ""; continue; }

                    // Rows are the objects that contain a "txt_Item" label, in screen order.
                    var rows = new System.Collections.Generic.List<Transform>();
                    foreach (var t in objs[cat].GetComponentsInChildren<TMP_Text>(false))
                        if (t.name == "txt_Item") rows.Add(t.transform.parent?.parent ?? t.transform);
                    int idx = menu.MenuItemIndex;
                    if (idx < 0 || idx >= rows.Count) continue;
                    var row = rows[idx];
                    string label = Label(row), value = Value(row);
                    string key = cat + ":" + idx;
                    if (key != _lastRowKey)
                    {
                        _lastRowKey = key; _lastValue = value;
                        Main.Log.Msg($"Option row {idx}/{rows.Count}: {label} = {value} ({row.name})");
                        Speech.Say(Menus.Join(label, value), true, false);
                    }
                    else if (value != _lastValue)
                    {
                        _lastValue = value;
                        Speech.Say(value, true, false);
                    }
                    return;
                }
            }
            catch (Exception e) { if (e.Message != _lastErr) { _lastErr = e.Message; Main.Log.Error("Options poll failed: " + e); } _nextScan = 0; }
        }

        private static string Label(Transform row)
        {
            foreach (var t in row.GetComponentsInChildren<TMP_Text>(false)) if (t.name == "txt_Item") return TextCleaner.Clean(t.text);
            return row.name;
        }

        private static string Value(Transform row)
        {
            foreach (var t in row.GetComponentsInChildren<TMP_Text>(false))
                if (t.name == "txt_Substance" && t.isActiveAndEnabled) return TextCleaner.Clean(t.text);
            var keys = row.GetComponentsInChildren<Il2CppGK_Common.UI.Option.GKOptionKeyConfigItem>(false);
            if (keys != null && keys.Length > 0)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var k in keys)
                {
                    try { var raw = k.m_TMP?.text; Main.Log.Msg("Key raw: " + raw); var c = TextCleaner.Clean(raw); if (c.Length > 0) parts.Add(c); } catch { }
                }
                if (parts.Count > 0) return string.Join(", ", parts);
            }
            var s = row.GetComponentInChildren<Slider>();
            if (s != null)
            {
                float range = s.maxValue - s.minValue;
                int pct = range > 0 ? (int)Math.Round((s.value - s.minValue) / range * 100) : 0;
                return pct + " percent";
            }
            var toggle = row.GetComponentInChildren<Toggle>();
            if (toggle != null) return toggle.isOn ? "on" : "off";
            return "";
        }
    }

    /// <summary>Title screen menu (Continue, Load, ...) which highlights without the shared button animation.</summary>
    internal static class TitleMenu
    {
        private static float _nextScan;
        private static Il2Cpp.GKMenu _menu;
        private static int _lastIdx = -99;
        private static int _lastTitle = -99;
        private static int _lastSub = -99;
        private static float _holdSubUntil;

        public static void Poll()
        {
            try
            {
                float now = Time.unscaledTime;
                if ((_menu == null || _menu.WasCollected) && now >= _nextScan)
                {
                    _nextScan = now + 1f;
                    _menu = UnityEngine.Object.FindObjectOfType<Il2Cpp.GKMenu>();
                }
                if (_menu == null || _menu.WasCollected) return;
                // Game selection screen (two game logos, no text).
                var sel = _menu.m_TitleSelect;
                if (sel != null && sel.activeInHierarchy)
                {
                    int t = _menu.m_SelectedTitleIdx;
                    if (t != _lastTitle)
                    {
                        _lastTitle = t;
                        Main.Log.Msg("Title select index " + t);
                        string game = t == 0 ? GameNames.GK1 : t == 1 ? GameNames.GK2 : "Game " + (t + 1);
                        // Say the game and the highlighted option together, so the option doesn't cut the game name off.
                        string option = "";
                        var subs0 = _menu.m_SubButtons;
                        int s0 = _menu.m_SelectedSubMenuIdx;
                        if (subs0 != null && s0 >= 0 && s0 < subs0.Length && subs0[s0] != null && subs0[s0].gameObject.activeInHierarchy)
                        {
                            option = Menus.Describe(subs0[s0]);
                            _lastSub = s0;
                        }
                        else _holdSubUntil = Time.unscaledTime + 1.5f;
                        Speech.Say(Menus.Join(game, option), true, false);
                    }
                }
                else _lastTitle = -99;

                // "New Game" / "Episode Select" under the chosen game.
                var subs = _menu.m_SubButtons;
                if (sel != null && sel.activeInHierarchy && subs != null)
                {
                    int s = _menu.m_SelectedSubMenuIdx;
                    if (s != _lastSub && Time.unscaledTime < _holdSubUntil && s >= 0 && s < subs.Length && subs[s] != null && subs[s].gameObject.activeInHierarchy)
                    {
                        // Options appeared just after the game name: queue instead of interrupting it.
                        _lastSub = s; _holdSubUntil = 0;
                        Speech.Say(Menus.Describe(subs[s]), interrupt: false);
                    }
                    else if (s != _lastSub && s >= 0 && s < subs.Length && subs[s] != null && subs[s].gameObject.activeInHierarchy)
                    {
                        _lastSub = s;
                        Menus.Focus(subs[s], "title sub " + s);
                    }
                }
                else _lastSub = -99;

                var obj = _menu.m_MenuButtonObj;
                var buttons = _menu.m_MainButtons;
                if (obj == null || !obj.activeInHierarchy || buttons == null) { _lastIdx = -99; return; }
                int idx = _menu.m_SelectedMenuIdx;
                if (idx == _lastIdx) return;
                _lastIdx = idx;
                if (idx < 0 || idx >= buttons.Length) return;
                var b = buttons[idx];
                if (b != null) Menus.Focus(b, "title " + idx);
            }
            catch (Exception e) { Main.Log.Error("Title poll failed: " + e.Message); _menu = null; }
        }
    }

    /// <summary>Help text shown at the bottom of menus ("txt_Info"): read after the focused item.</summary>
    internal static class Tooltips
    {
        private static float _nextScan;
        private static TMP_Text[] _infos = new TMP_Text[0];
        private static string _last = "";
        private static string _pending;
        private static float _pendingAt;

        public static void Poll()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now >= _nextScan)
                {
                    _nextScan = now + 0.5f;
                    var list = new System.Collections.Generic.List<TMP_Text>();
                    foreach (var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
                        if (t.name == "txt_Info") list.Add(t);
                    _infos = list.ToArray();
                }
                string cur = "";
                foreach (var t in _infos)
                    if (t != null && !t.WasCollected && t.isActiveAndEnabled) { cur = TextCleaner.Clean(t.text); break; }
                if (cur != _last) { _last = cur; _pending = cur; _pendingAt = now + 0.2f; }
                // Wait a moment so the focused item is announced first, then queue the help text after it.
                if (_pending != null && now >= _pendingAt)
                {
                    if (!string.IsNullOrEmpty(_pending)) Speech.Say(_pending, interrupt: false, remember: false);
                    _pending = null;
                }
            }
            catch (Exception e) { Main.Log.Error("Tooltip poll failed: " + e.Message); _nextScan = 0; }
        }
    }
}

namespace AAIAccess
{
    internal static class GameNames
    {
        public const string GK1 = "Ace Attorney Investigations: Miles Edgeworth";
        public const string GK2 = "Ace Attorney Investigations 2: Prosecutor's Gambit";
    }

    /// <summary>Reads pop-up dialog messages (alerts, confirmations, notices) as soon as they appear.</summary>
    internal static class DialogWatcher
    {
        public static string LastMessage = "";

        /// <summary>Record that a dialog message was already read (together with its button).</summary>
        public static void MarkRead(string msg)
        {
            if (msg != LastMessage) LastReadAt = UnityEngine.Time.unscaledTime;
            LastMessage = msg; _absentSince = -1f; _graceUntil = UnityEngine.Time.unscaledTime + 1.5f;
        }
        public static float LastReadAt = -10f;
        private static float _graceUntil;
        private static float _nextScan;
        private static Il2CppTMPro.TMP_Text[] _texts = new Il2CppTMPro.TMP_Text[0];
        private static string _pending;
        private static float _pendingAt;

        // Message labels in dialog boxes: txt_Dialog_Text, txt_Dialog_Text_NoneHeadline, ...
        private static bool IsDialogText(Il2CppTMPro.TMP_Text t) => t.name.StartsWith("txt_Dialog_Text"); // not the story text box, which the dialogue reader handles
        private static float _absentSince = -1f;

        public static string MessageUnder(UnityEngine.Transform root)
        {
            string best = ""; int bestScore = -1;
            foreach (var t in root.GetComponentsInChildren<Il2CppTMPro.TMP_Text>(false))
            {
                if (!IsDialogText(t) || !t.isActiveAndEnabled) continue;
                string s = TextCleaner.Clean(t.text);
                if (s.Length == 0) continue;
                int sc = LangScore(s);   // some screens keep hidden Japanese copies of messages: prefer the player's language
                if (sc > bestScore) { best = s; bestScore = sc; }
            }
            return best;
        }

        internal static bool HasLatin(string s)
        {
            foreach (char c in s) if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true;
            return false;
        }

        /// <summary>Which copy of a message to read when a screen holds several: the game keeps hidden Japanese copies
        /// next to the player's language. Latin text first (English, French, German), then Korean, then Chinese
        /// (Han characters without kana), then text with kana (the Japanese copies; only chosen when it's all there is).</summary>
        /// <summary>The language score of the last focused button with letters in it (0 = none yet).</summary>
        internal static int UiScore;

        /// <summary>Japanese text (with kana) while the game's own buttons are in another language: a hidden copy.</summary>
        internal static bool IsForeignCopy(string s, int uiScore) => uiScore > 1 && LangScore(s) == 1;

        internal static int LangScore(string s)
        {
            bool latin = false, hangul = false, kana = false, han = false;
            foreach (char c in s)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) latin = true;
                else if (c >= 0xAC00 && c <= 0xD7AF) hangul = true;
                else if (c >= 0x3040 && c <= 0x30FF) kana = true;
                else if (c >= 0x4E00 && c <= 0x9FFF) han = true;
            }
            return latin ? 4 : hangul ? 3 : han && !kana ? 2 : kana ? 1 : 0;
        }

        public static void Poll()
        {
            try
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now >= _nextScan)
                {
                    _nextScan = now + 0.3f;
                    var list = new System.Collections.Generic.List<Il2CppTMPro.TMP_Text>();
                    foreach (var t in UnityEngine.Object.FindObjectsOfType<Il2CppTMPro.TMP_Text>())
                        if (IsDialogText(t)) list.Add(t);
                    _texts = list.ToArray();
                }
                string cur = ""; int curScore = -1;
                foreach (var t in _texts)
                {
                    if (t == null || t.WasCollected || !t.isActiveAndEnabled) continue;
                    string s = TextCleaner.Clean(t.text);
                    if (s.Length == 0) continue;
                    int sc = LangScore(s);
                    if (sc > curScore) { cur = s; curScore = sc; }
                }

                if (!string.IsNullOrEmpty(cur) && IsForeignCopy(cur, UiScore)) cur = "";
                if (string.IsNullOrEmpty(cur))
                {
                    // Dialogs flicker while opening; only forget the last message once it has been gone for a second.
                    if (_absentSince < 0) _absentSince = now;
                    else if (now - _absentSince > 1f && now > _graceUntil) LastMessage = "";
                    _pending = null; return;
                }
                _absentSince = -1f;
                if (cur == LastMessage) { _pending = null; return; }
                // New message: give the focused button a moment to announce it together with the button.
                if (_pending != cur) { _pending = cur; _pendingAt = now + 0.3f; return; }
                if (now >= _pendingAt)
                {
                    LastMessage = cur; _pending = null;
                    Main.Log.Msg("Dialog: " + cur);
                    Speech.Say(cur, interrupt: false);
                }
            }
            catch (System.Exception e) { Main.Log.Error("Dialog poll failed: " + e.Message); _nextScan = 0; }
        }
    }
}
