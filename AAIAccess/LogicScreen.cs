using System;
using Il2CppGK_Common.UI.Logic;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>
    /// Logic screen (both games): reads the highlighted piece of Logic with its description,
    /// announces when pieces are picked or removed, and F3 reads the current selection.
    /// </summary>
    internal static class LogicScreen
    {
        private static GKLogicWindow _win;
        private static float _nextScan;
        private static bool _open;
        private static int _lastCursor = -99;
        private static int _lastIdA = -1, _lastIdB = -1;
        private static bool _skipCursorSay;

        private static bool IsOpen(GKLogicWindow w) =>
            w != null && !w.WasCollected && w.gameObject.activeInHierarchy && !w.m_IsClose;

        public static bool Active => _open;

        public static void Poll()
        {
            try
            {
                float now = Time.unscaledTime;
                if ((_win == null || _win.WasCollected) && now >= _nextScan)
                {
                    _nextScan = now + 1f;
                    _win = UnityEngine.Object.FindObjectOfType<GKLogicWindow>();
                }
                bool open = IsOpen(_win);
                if (!open) { if (_open) Main.Log.Msg("Logic closed"); _open = false; _lastCursor = -99; _lastIdA = _lastIdB = -1; return; }

                if (!_open)
                {
                    _open = true;
                    int n = _win.m_LogicWordData?.Count ?? 0;
                    Main.Log.Msg($"Logic opened with {n} pieces");
                    Speech.Announce($"Logic. {n} pieces. Press F1 for help.");
                }

                // Picked pieces: the game keeps their IDs (the top panels only preview the highlighted piece).
                int ida = _win.m_Select_ID_A, idb = _win.m_Select_ID_B;
                if (ida != _lastIdA || idb != _lastIdB)
                {
                    Main.Log.Msg($"Logic picks: A={ida} B={idb}");
                    string a = NameForId(ida), b = NameForId(idb);
                    string oldA = NameForId(_lastIdA), oldB = NameForId(_lastIdB);
                    string msg = "";
                    if (a.Length > 0 && b.Length > 0) msg = $"{(a != oldA && a != oldB ? a : b)} picked. Ready to connect {a} and {b}.";
                    else if (a.Length > 0 || b.Length > 0)
                    {
                        string one = a.Length > 0 ? a : b;
                        msg = (one == oldA || one == oldB) && (oldA.Length > 0 && oldB.Length > 0) ? "Removed. " + one + " still picked." : one + " picked. Pick one more.";
                    }
                    else if (oldA.Length > 0 || oldB.Length > 0) msg = "Nothing picked.";
                    _lastIdA = ida; _lastIdB = idb;
                    if (msg.Length > 0) Speech.Say(msg);
                    _skipCursorSay = true;   // the cursor announcement right after a pick would repeat the name
                }

                // Highlighted piece in the list.
                int cur = _win.m_CursorIdx;
                if (cur != _lastCursor)
                {
                    _lastCursor = cur;
                    string name = WordName(cur);
                    Main.Log.Msg($"Logic cursor {cur}: {name}");
                    if (name.Length > 0 && !_skipCursorSay)
                    {
                        bool picked = name == NameForId(_lastIdA) || name == NameForId(_lastIdB);
                        Speech.Say(Menus.Join(name, picked ? "picked" : ""), true, false);
                        string desc = Description(name);
                        if (desc.Length > 0) Speech.Say(desc, interrupt: false, remember: false);
                    }
                }
                _skipCursorSay = false;
            }
            catch (Exception e) { Main.Log.Error("Logic poll failed: " + e.Message); _win = null; _nextScan = Time.unscaledTime + 1f; }
        }

        /// <summary>The game's pick values are positions in the Logic list (-1 = nothing picked).</summary>
        private static string NameForId(int id)
        {
            if (id < 0 || _win == null) return "";
            return WordName(id);
        }

        private static string SlotTitle(int i)
        {
            var slots = _win.m_SelectObj;
            if (slots == null || i >= slots.Length || slots[i] == null || !slots[i].gameObject.activeInHierarchy) return "";
            var t = slots[i].m_Title;
            return t != null && t.gameObject.activeInHierarchy ? TextCleaner.Clean(t.text) : "";
        }

        private static string WordName(int idx)
        {
            var words = _win.m_WordObj;
            if (words != null && idx >= 0 && idx < words.Length && words[idx] != null && words[idx].m_Text != null)
            {
                string s = TextCleaner.Clean(words[idx].m_Text.text);
                if (s.Length > 0) return s;
            }
            var data = _win.m_LogicWordData;
            if (data != null && idx >= 0 && idx < data.Count && data[idx] != null)
                return TextCleaner.Clean(data[idx].m_ButtonTitle ?? data[idx].m_Title);
            return "";
        }

        private static string Description(string name)
        {
            var data = _win.m_LogicWordData;
            if (data == null) return "";
            foreach (var d in data)
            {
                if (d == null) continue;
                if (TextCleaner.Clean(d.m_ButtonTitle) == name || TextCleaner.Clean(d.m_Title) == name)
                    return TextCleaner.Clean(d.m_Desc);
            }
            return "";
        }

        /// <summary>F3: say what is picked and what is highlighted.</summary>
        public static void ReadStatus()
        {
            if (!_open) { Speech.Say("Not in Logic.", true, false); return; }
            string a = NameForId(_win.m_Select_ID_A), b = NameForId(_win.m_Select_ID_B);
            string picked = a.Length == 0 && b.Length == 0 ? "Nothing picked." :
                            a.Length > 0 && b.Length > 0 ? $"Picked {a} and {b}." : $"Picked {(a.Length > 0 ? a : b)}. Pick one more.";
            string cur = WordName(_win.m_CursorIdx);
            Speech.Say(Menus.Join(picked, cur.Length > 0 ? "Highlighted: " + cur : ""), true, false);
        }
    }
}
