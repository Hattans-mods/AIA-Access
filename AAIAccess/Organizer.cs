using System;
using Il2CppGK_Common.UI;
using Il2CppTMPro;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>
    /// The Organizer (evidence and profiles). The game shows the highlighted entry's name, age and
    /// description in text fields; the mod reads them whenever the highlighted entry or the tab changes.
    /// </summary>
    internal static class Organizer
    {
        private static GKSousaListDialog[] _dialogs = new GKSousaListDialog[0];
        private static float _nextScan;
        private static bool _open;
        private static string _lastEntry = "", _lastTab = "";
        private static float _lastChange = -10f;
        private static string _pending;
        private static float _pendingAt;

        public static bool Active => _open;
        /// <summary>The last entry the player looked at (its name).</summary>
        public static string LastName { get; private set; } = "";
        /// <summary>The game's item number of that entry (-1 when unknown).</summary>
        public static int LastItemId { get; private set; } = -1;

        private static GKSousaListDialog Current()
        {
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + 0.5f;
                var f = UnityEngine.Object.FindObjectsOfType<GKSousaListDialog>();
                _dialogs = new GKSousaListDialog[f.Length];
                for (int i = 0; i < f.Length; i++) _dialogs[i] = f[i];
            }
            foreach (var d in _dialogs)
                if (d != null && !d.WasCollected && d.isActiveAndEnabled && !d.IsClose()) return d;
            return null;
        }

        private static string T(TMP_Text t)
        {
            try { return t != null && t.isActiveAndEnabled ? TextCleaner.Clean(t.text) : ""; } catch { return ""; }
        }

        private static string TabName(GKSousaListDialog d)
        {
            // The highlighted entry says whether it is evidence or a person.
            try
            {
                string title = T(d.TitleString);
                var list = d.ItemList;
                if (list != null && title.Length > 0)
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        if (e != null && TextCleaner.Clean(e.itemName) == title) return e.isItem ? "Evidence" : "Profiles";
                    }
            }
            catch { }
            try
            {
                var m = d.GetTabMode();
                if (m == GKSousaListDialog.ModeType.MODE_SYOUKO) return "Evidence";
                if (m == GKSousaListDialog.ModeType.MODE_JINBUTSU) return "Profiles";
                return m.ToString();
            }
            catch { return ""; }
        }

        public static void Poll()
        {
            try
            {
                var d = Current();
                float now = Time.unscaledTime;
                if (d == null)
                {
                    if (_open) { _open = false; Main.Log.Msg("Organizer closed"); _lastEntry = ""; _lastTab = ""; }
                    return;
                }
                string tab = TabName(d);
                string name = T(d.TitleString), age = T(d.AgeString), desc = T(d.ItemString);
                string entry = name + "|" + desc;
                if (!_open)
                {
                    _open = true;
                    bool present = false; try { present = d.IsThrustMode(); } catch { }
                    Main.Log.Msg($"Organizer opened: tab={tab} present={present} items={d.itemCnt} panels={d.ItemPanel?.Length}");
                    _lastTab = tab; _lastEntry = "";
                    // Presenting: the question first (read now if it hasn't been yet, and not cut off), then the
                    // screen, then the highlighted evidence.
                    if (present) Dialogue.FlushPending();
                    Speech.Announce(Menus.Join(present ? "Present evidence" : "Organizer", tab, Help.Hint()), !present);
                }
                if (tab != _lastTab) { _lastTab = tab; _lastEntry = ""; Speech.Announce(tab, true); }
                if (entry != _lastEntry && !string.IsNullOrEmpty(name))
                {
                    _lastEntry = entry; LastName = name;
                    LastItemId = -1;
                    try
                    {
                        var list = d.ItemList;
                        for (int i = 0; list != null && i < list.Count; i++)
                            if (list[i] != null && TextCleaner.Clean(list[i].itemName) == name) { LastItemId = list[i].itemId; break; }
                    }
                    catch { }
                    int pos = -1, total = 0;
                    try { pos = d.GetPage() * Math.Max(1, d.ItemPanel?.Length ?? 1) + d.GetCursor(); total = d.itemCnt; } catch { }
                    Main.Log.Msg($"Organizer entry {pos + 1}/{total}: {name} | {age} | {desc}");
                    // Wait a moment: the fields fill in one after another when the cursor moves.
                    _pending = Menus.Join(name, age, desc, pos >= 0 && total > 0 ? $"{pos + 1} of {total}" : "");
                    _pendingAt = now + 0.1f;
                }
                if (_pending != null && now >= _pendingAt)
                {
                    Speech.Say(_pending, true, false);
                    _pending = null;
                }
            }
            catch (Exception e) { Main.Log.Error("Organizer poll failed: " + e.Message); _nextScan = Time.unscaledTime + 1f; }
        }

        /// <summary>How to switch tabs, when the Organizer has them. The game's own hint is a button picture (read as
        /// "controller button 5"), so the mod names the keys itself: Q and E, or the controller's shoulder buttons
        /// (the game's L1 and R1).</summary>
        public static string TabKeys(bool pad = false)
        {
            var d = Current();
            if (d == null) return "";
            string l = T(d.Tab_L_KeyGuide), r = T(d.Tab_R_KeyGuide);
            if (l.Length == 0 && r.Length == 0) return "";
            return Pad.F("Press {0} and {1} to switch between evidence and profiles.",
                pad ? Pad.Name(Pad.B.L1) : KeyMap.Name(Il2CppGK_System.Controller.ButtonType.L1),
                pad ? Pad.Name(Pad.B.R1) : KeyMap.Name(Il2CppGK_System.Controller.ButtonType.R1));
        }

        /// <summary>F3: the highlighted entry again.</summary>
        public static void ReadStatus()
        {
            var d = Current();
            if (d == null) return;
            Speech.Say(Menus.Join(TabName(d), T(d.TitleString), T(d.AgeString), T(d.ItemString)), true, false);
        }
    }
}
