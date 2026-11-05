extern alias GK1;
using System;
using System.Text;
using Il2CppGK_Common.UI.SousaPart;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>Investigation: announces the Examine / Talk prompt that appears near people and objects.</summary>
    internal static class Investigation
    {
        private static float _nextScan;
        private static SousaPlayerActionButton[] _buttons = new SousaPlayerActionButton[0];
        private static string _lastPrompt = "";
        /// <summary>The Examine / Talk prompt showing right now ("" when none).</summary>
        public static string CurrentPrompt => _lastPrompt;
        private static string _pendingPrompt;

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        private static bool WalkKeyHeld()
        {
            if (Navigator.HoldingKeys) return false;   // the mod is walking for you: speak right away
            foreach (int k in new[] { 0x57, 0x41, 0x53, 0x44, 0x25, 0x26, 0x27, 0x28 })   // W A S D and arrows
                if ((GetAsyncKeyState(k) & 0x8000) != 0) return true;
            return false;
        }
        private static float _lastSeenButtons = -100f;

        /// <summary>True while the investigation walking screen is up (its action button exists).</summary>
        public static bool InInvestigation => UnityEngine.Time.unscaledTime - _lastSeenButtons < 2f || PlayerCanWalk;

        /// <summary>The game lets you walk Edgeworth around right now (no dialogue, menu or close-up).</summary>
        public static bool PlayerCanWalk
        {
            get
            {
                try
                {
                    var g = GK1::Il2Cpp.AppCore.gkj1;
                    return g != null && g.GetMyCharBuff() != null && g.GetMyCharCtrlFlg() && !ExamineScreen.Active && !LogicScreen.Active;
                }
                catch { return false; }
            }
        }

        public static void Poll()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now >= _nextScan)
                {
                    _nextScan = now + 0.5f;
                    var f = UnityEngine.Object.FindObjectsOfType<SousaPlayerActionButton>();
                    _buttons = new SousaPlayerActionButton[f.Length];
                    for (int i = 0; i < f.Length; i++) _buttons[i] = f[i];
                }
                string prompt = "";
                foreach (var b in _buttons)
                {
                    if (b != null && !b.WasCollected && b.gameObject.activeInHierarchy) _lastSeenButtons = now;
                    if (b == null || b.WasCollected || !b.gameObject.activeInHierarchy || !b.IsOpen) continue;
                    var type = b.CurrentIconType;
                    if (type == SousaPlayerActionButton.IconType.None) continue;
                    prompt = type == SousaPlayerActionButton.IconType.Talk ? "Talk" : "Examine";
                    try { if (b.m_CheckMarkObj != null && b.m_CheckMarkObj.activeInHierarchy) prompt += ", checked"; } catch { }
                    break;
                }
                if (prompt != _lastPrompt)
                {
                    _lastPrompt = prompt;
                    if (prompt.Length > 0)
                    {
                        Main.Log.Msg("Action prompt: " + prompt);
                        bool talk = prompt.StartsWith("Talk"), done = prompt.EndsWith("checked");
                        Earcons.Play(talk ? (done ? Earcons.TalkChecked : Earcons.Talk) : (done ? Earcons.ExamineChecked : Earcons.Examine));
                        _pendingPrompt = prompt;
                    }
                    else _pendingPrompt = null;
                }
                // NVDA cuts speech off on every key press, so wait until the walk keys are released.
                if (_pendingPrompt != null && !WalkKeyHeld())
                {
                    // Say what it's for, when that's clear: "Examine horse, checked".
                    bool talk = _pendingPrompt.StartsWith("Talk"), done = _pendingPrompt.EndsWith("checked");
                    string owner = Navigator.OwnerName(talk);
                    string say = owner.Length > 0 ? (talk ? "Talk to " : "Examine ") + owner + (done ? ", checked" : "") : _pendingPrompt;
                    Speech.Say(say, interrupt: false, remember: false);
                    _pendingPrompt = null;
                }
            }
            catch (Exception e) { Main.Log.Error("Investigation poll failed: " + e.Message); _nextScan = Time.unscaledTime + 1f; }
        }

        /// <summary>F10: write the player's position and every map object (first game) to the log.</summary>
        public static void DumpMap()
        {
            try
            {
                var g = GK1::Il2Cpp.AppCore.gkj1;
                if (g == null) { Speech.Say("No map data.", true, false); return; }
                var sb = new StringBuilder();
                sb.AppendLine("==== MAP DUMP ====");
                var me = g.GetMyCharBuff();
                if (me != null) sb.AppendLine($"ME num={me.num} x={me.xof} y={me.yof} z={me.zof} dir={me.dir} sts={me.sts:X} dsp={me.dsp:X}");
                var rec = g.GetRecordData();
                if (rec != null)
                {
                    var mi = rec.mapinf_data;
                    if (mi != null) sb.AppendLine($"MAP mymap={mi.mymap} myarea={mi.myarea} map_px={rec.map_px} map_py={rec.map_py} mode={rec.mode_main}");
                    var mb = rec.mapbuff_data;
                    if (mb != null)
                    {
                        sb.AppendLine($"BUF bg={mb.mapbg_no} size={mb.map_sx}x{mb.map_sy}");
                        var zones = mb.zone_tbl;
                        if (zones != null)
                            for (int i = 0; i < zones.Length; i++)
                            {
                                var z = zones[i]; if (z == null) continue;
                                sb.AppendLine($"ZONE[{i}] sts={z.sts:X} num={z.num} x={z.xof} y={z.yof} zone={z.zone} pos={z.posx},{z.posy} icon={z.iconnum} ctrl={z.ctrltype} disp={z.disptype} {Rect(z.flg)} hit{Rect(z.hit)}");
                            }
                    }
                }
                var objs = g.GetMapObjData();
                if (objs != null)
                    for (int i = 0; i < objs.Length; i++)
                    {
                        var o = objs[i];
                        if (o == null || o.sts == 0) continue;
                        sb.AppendLine($"OBJ[{i}] num={o.num} {ObjNames.Raw(o.num)} callA={o.call_a:X} callX={o.call_x:X} x={o.xof} y={o.yof} z={o.zof} dir={o.dir} sts={o.sts:X} dsp={o.dsp:X} icon={o.iconnum}@{o.iconx},{o.icony} ctrl={o.ctrltype} disp={o.disptype} anime={o.AnimeNum} {Rect(o.flg)} hit{Rect(o.hit)}");
                    }
                sb.AppendLine("==== END MAP ====");
                Main.Log.Msg(sb.ToString());
                Speech.Say("Map saved to log.", true, false);
            }
            catch (Exception e) { Main.Log.Error("Map dump failed: " + e); Speech.Say("Map dump failed.", true, false); }
        }

        private static string Rect(GK1::Il2Cpp.OBJ_ATTRI a) =>
            a == null ? "[]" : $"[{a.lux},{a.luy} {a.rux},{a.ruy} {a.ldx},{a.ldy} {a.rdx},{a.rdy}]";
    }
}
