using System;
using System.Text;
using HarmonyLib;
using Il2CppGK_Common.UI;
using Il2CppGK_Common.UI.Choices;
using Il2CppTMPro;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>
    /// Announces the highlighted menu item. Most menus in the game are built from GKUIButton,
    /// which shows focus by playing its "Highlighted"/"Selected" animation.
    /// </summary>
    internal static class Menus
    {
        private static IntPtr _lastButton = IntPtr.Zero;
        private static string _lastText = "";
        private static float _lastTime;

        // Choices: also watched by index, in case a menu moves the cursor without animating.
        private static float _nextScan;
        private static ChoicesBase[] _choices = new ChoicesBase[0];
        private static IntPtr _lastChoices = IntPtr.Zero;
        private static int _lastChoiceIndex = -1;


        private static string Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
                if (t.name == name) return TextCleaner.Clean(t.text);
            return "";
        }

        /// <summary>Full description of a button. Save slots get their details read in a useful order.</summary>
        public static string Describe(GKUIButton b)
        {
            try
            {
                if (b.gameObject.name.StartsWith("SaveSlot"))
                {
                    var tr = b.transform;
                    string ep = Find(tr, "txt_EpisodeNumber");
                    if (string.IsNullOrEmpty(ep) || !IsShown(tr, "txt_EpisodeNumber"))
                        return Join(SlotLabel(tr), ButtonText(b));
                    return Join(SlotLabel(tr), Find(tr, "txt_TitleName"), ep, Find(tr, "txt_EpisodeTitle"), Find(tr, "txt_ChapterTitle"), Find(tr, "txt_Date"));
                }
            }
            catch { }
            string n = b.gameObject.name;
            string chess = LogicChess.Describe(b);
            if (chess != null) return chess;
            string topic = TopicChecked(b);
            if (topic != null) return topic;
            if (n.EndsWith("_GK1")) return GameNames.GK1;
            if (n.EndsWith("_GK2")) return GameNames.GK2;
            return ButtonText(b);
        }

        /// <summary>
        /// Talk topics (PartnerChoices): the game puts a check mark next to topics you've already talked about.
        /// Returns "Topic, checked" / "Topic", or null if the button isn't a topic.
        /// </summary>
        private static string TopicChecked(GKUIButton b)
        {
            try
            {
                var pc = b.GetComponentInParent<PartnerChoices>();
                if (pc == null)
                {
                    // Other choice lists: look for a check mark object inside the button.
                    var cb = b.GetComponentInParent<ChoicesBase>();
                    if (cb == null) return null;
                    LogChoiceShape(cb);
                    foreach (var tr in b.GetComponentsInChildren<Transform>(false))
                        if (tr != b.transform && tr.name.IndexOf("check", StringComparison.OrdinalIgnoreCase) >= 0) { string bt = ButtonText(b); return string.IsNullOrEmpty(bt) ? null : bt + ", checked"; }
                    return null;
                }
                LogChoiceShape(pc);
                // Each topic button carries its own "CheckMark / On_CheckMark" objects.
                bool done = false;
                var info = new StringBuilder();
                foreach (var tr in b.GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name != "On_CheckMark") continue;
                    float alpha = 1f;
                    try { var cg = tr.GetComponentInParent<CanvasGroup>(); if (cg != null) alpha = cg.alpha; } catch { }
                    try { var gr = tr.GetComponentInChildren<UnityEngine.UI.Graphic>(); if (gr != null) alpha = Mathf.Min(alpha, gr.color.a); } catch { }
                    info.Append($"{tr.name} active={tr.gameObject.activeInHierarchy} alpha={alpha:0.00}; ");
                    if (tr.gameObject.activeInHierarchy && alpha > 0.1f) done = true;
                }
                // The game's own list of marks, as a second opinion.
                try
                {
                    var buttons = pc.m_Buttons; int idx = -1;
                    for (int i = 0; buttons != null && i < buttons.Count; i++) if (buttons[i] != null && buttons[i].Pointer == b.Pointer) { idx = i; break; }
                    var marks = pc.m_CheckMarks; int o = idx < 0 ? -1 : pc.GetObjectIndex(idx);
                    info.Append($"index={idx} obj={o} listMark={(marks != null && o >= 0 && o < marks.Count && marks[o] != null ? marks[o].activeInHierarchy.ToString() : "?")}");
                }
                catch { }
                string t = ButtonText(b);
                if (info.ToString() != _lastTopicInfo) { _lastTopicInfo = info.ToString(); Main.Log.Msg($"Topic '{t}': {info}"); }
                return string.IsNullOrEmpty(t) ? null : (done ? t + ", checked" : t);
            }
            catch (Exception e) { Main.Log.Warning("Topic check failed: " + e.Message); return null; }
        }

        private static IntPtr _loggedChoice = IntPtr.Zero;
        private static string _lastTopicInfo = "";
        /// <summary>Once per choice list: write its layout to the log (to find where check marks live).</summary>
        private static void LogChoiceShape(Component c)
        {
            if (c.Pointer == _loggedChoice) return;
            _loggedChoice = c.Pointer;
            var sb = new StringBuilder($"Choice list {c.GetIl2CppType().Name}:");
            foreach (var tr in c.GetComponentsInChildren<Transform>(true))
                if (tr.name.IndexOf("check", StringComparison.OrdinalIgnoreCase) >= 0 || tr.name.IndexOf("mark", StringComparison.OrdinalIgnoreCase) >= 0 || tr.name.StartsWith("Button"))
                    sb.Append($" [{tr.parent?.name}/{tr.name} {(tr.gameObject.activeInHierarchy ? "on" : "off")}]");
            Main.Log.Msg(sb.ToString());
        }

        private static string SlotLabel(Transform tr)
        {
            string n = Find(tr, "txt_Number");
            if (tr.name.Contains("AutoSave")) return "Auto save";
            return string.IsNullOrEmpty(n) ? "" : "Slot " + n;
        }

        private static bool IsShown(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
                if (t.name == name) return t.isActiveAndEnabled;
            return false;
        }

        internal static string Join(params string[] parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(p);
            }
            return sb.ToString();
        }

        /// <summary>If the button belongs to a dialog box (alert / confirmation), returns its message.</summary>
        private static string DialogMessage(GKUIButton b)
        {
            var t = b.transform.parent;
            for (int i = 0; i < 6 && t != null; i++, t = t.parent)
            {
                string m = DialogWatcher.MessageUnder(t);
                if (!string.IsNullOrEmpty(m)) return m;
            }
            return "";
        }

        public static string ButtonText(GKUIButton b)
        {
            try
            {
                TMP_Text t = b.m_Text;
                if (t == null) t = b.GetComponentInChildren<TMP_Text>();
                return t == null ? "" : TextCleaner.Clean(t.text);
            }
            catch { return ""; }
        }

        /// <summary>Speak a focused item once (ignores repeats of the same button within half a second).</summary>
        public static void Focus(GKUIButton b, string source)
        {
            if (b == null || !b.gameObject.activeInHierarchy) return;
            string text = Describe(b);
            if (string.IsNullOrEmpty(text)) return;
            float now = Time.unscaledTime;
            // Some buttons replay their highlight animation continuously: stay quiet while it repeats.
            if (b.Pointer == _lastButton && text == _lastText && now - _lastTime < 1f) { _lastTime = now; return; }
            Main.Log.Msg($"Focus ({source}): '{text}' on {b.gameObject.name}");
            _lastButton = b.Pointer; _lastText = text; _lastTime = now;
            // First focus inside a newly shown dialog: read its message before the button.
            string msg = DialogMessage(b);
            // The episode screen keeps a hidden Japanese copy of its question ("このチャプターから…") even when the
            // game is in another language: never read Japanese next to a button that isn't in Japanese.
            int btnScore = DialogWatcher.LangScore(text);
            if (btnScore > 0) DialogWatcher.UiScore = btnScore;
            if (!string.IsNullOrEmpty(msg) && DialogWatcher.IsForeignCopy(msg, btnScore)) { Main.Log.Msg("Skipped hidden Japanese copy: " + msg); msg = ""; }
            if (!string.IsNullOrEmpty(msg) && (msg != DialogWatcher.LastMessage || now - DialogWatcher.LastReadAt < 0.5f))
            {
                // New dialog, or the game moved the cursor straight after opening it: keep the message in front.
                text = msg + " " + text; DialogWatcher.MarkRead(msg); Speech.Remember(msg);
            }
            // A choice list: make sure its question is read first.
            bool asked = false;
            try { if (b.GetComponentInParent<ChoicesBase>() != null) asked = Dialogue.FlushPending(); } catch { }
            // Don't cut off a line of dialogue that was only just read (e.g. the question before choices).
            bool interrupt = !asked && now - Dialogue.LastSpokenAt > 2f;
            if (EpisodeMenus.EpisodeScreenActive) { EpisodeMenus.DeferButton(text); return; }
            Speech.Say(text, interrupt, false);   // F2 repeats the question, not the highlighted item
            // Save slots refresh their details a moment after saving: check again shortly.
            if (b.gameObject.name.StartsWith("SaveSlot")) { _slot = b; _slotText = Describe(b); _slotCheckAt = now + 0.6f; }
        }

        private static GKUIButton _slot;
        private static string _slotText;
        private static float _slotCheckAt = -1f;

        private static void RecheckSlot()
        {
            if (_slotCheckAt < 0 || Time.unscaledTime < _slotCheckAt) return;
            _slotCheckAt = -1f;
            try
            {
                if (_slot == null || _slot.WasCollected || !_slot.gameObject.activeInHierarchy || _slot.Pointer != _lastButton) return;
                string now = Describe(_slot);
                if (!string.IsNullOrEmpty(now) && now != _slotText)
                {
                    Main.Log.Msg($"Save slot updated: '{now}'");
                    _lastText = now;
                    Speech.Say(now, false);
                }
            }
            catch { }
        }

        public static void Poll()
        {
            RecheckSlot();
            DialogWatcher.Poll();
            EpisodeMenus.Poll();
            OptionsMenu.Poll();
            TitleMenu.Poll();
            Tooltips.Poll();
            try
            {
                float now = Time.unscaledTime;
                if (now >= _nextScan)
                {
                    _nextScan = now + 0.5f;
                    var found = UnityEngine.Object.FindObjectsOfType<ChoicesBase>();
                    _choices = new ChoicesBase[found.Length];
                    for (int i = 0; i < found.Length; i++) _choices[i] = found[i];
                }
                foreach (var c in _choices)
                {
                    if (c == null || c.WasCollected || !c.isActiveAndEnabled) continue;
                    var buttons = c.m_Buttons;
                    int idx = c.m_SelectButtonIndex;
                    if (buttons == null || idx < 0 || idx >= buttons.Count) continue;
                    if (c.Pointer == _lastChoices && idx == _lastChoiceIndex) return;
                    bool newMenu = c.Pointer != _lastChoices;
                    _lastChoices = c.Pointer; _lastChoiceIndex = idx;
                    var b = buttons[idx];
                    if (newMenu) Main.Log.Msg($"Choices opened: {c.GetIl2CppType().Name} with {buttons.Count} options");
                    Focus(b, "choices");
                    return;
                }
                _lastChoices = IntPtr.Zero; _lastChoiceIndex = -1;
            }
            catch (Exception e) { Main.Log.Error("Menu poll failed: " + e.Message); _nextScan = 0; }
        }
    }

    [HarmonyPatch(typeof(GKUIButton), nameof(GKUIButton.ButtonAnimePlay), new[] { typeof(GKUIButton.AnimeType), typeof(bool) })]
    internal static class Patch_ButtonAnimeType
    {
        private static void Postfix(GKUIButton __instance, GKUIButton.AnimeType type)
        {
            try
            {
                if (type == GKUIButton.AnimeType.Highlighted || type == GKUIButton.AnimeType.Selected)
                    Menus.Focus(__instance, "anim " + type);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GKUIButton), nameof(GKUIButton.ButtonAnimePlay), new[] { typeof(string), typeof(bool) })]
    internal static class Patch_ButtonAnimeName
    {
        private static void Postfix(GKUIButton __instance, string anime)
        {
            try
            {
                if (!string.IsNullOrEmpty(anime) && anime == __instance.m_Forcus)
                    Menus.Focus(__instance, "anim " + anime);
            }
            catch { }
        }
    }

    /// <summary>F11: writes every visible text on screen, with its object path, to the MelonLoader log.</summary>
    internal static class DebugDump
    {
        public static void Dump()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("==== SCREEN DUMP ====");
                foreach (var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
                {
                    if (t == null || !t.isActiveAndEnabled) continue;
                    string s = TextCleaner.Clean(t.text);
                    if (string.IsNullOrEmpty(s)) continue;
                    sb.AppendLine($"[{Path(t.transform)}] {s}");
                }
                foreach (var b in UnityEngine.Object.FindObjectsOfType<GKUIButton>())
                    if (b != null && b.gameObject.activeInHierarchy)
                        sb.AppendLine($"BUTTON [{Path(b.transform)}] '{Menus.ButtonText(b)}'");
                foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                    if (mb != null && mb.isActiveAndEnabled)
                    {
                        string n = mb.GetIl2CppType().FullName;
                        if (!n.StartsWith("UnityEngine") && !n.StartsWith("TMPro") && !n.StartsWith("Spine"))
                            sb.AppendLine($"COMPONENT {n} on [{Path(mb.transform)}]");
                    }
                sb.AppendLine("==== END DUMP ====");
                Main.Log.Msg(sb.ToString());
                Speech.Say("Screen saved to log.", true, false);
            }
            catch (Exception e) { Main.Log.Error("Dump failed: " + e); }
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }
    }
}
