extern alias GK1;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>
    /// Room names (first game). The game never shows the name of the room you walk into, so the mod
    /// guesses it from the things in the room (their internal names start with a code for the place,
    /// e.g. OF_ = your office) and you can rename a room yourself: copy a name, then press Shift+F4.
    /// The name is said when you enter a room; F4 says it again.
    /// </summary>
    internal static class Rooms
    {
        // Place codes of the objects -> a name for the room.
        private static readonly Dictionary<string, string> Codes = new Dictionary<string, string>
        {
            { "OF", "your office" }, { "CRD00", "hallway outside your office" },
            { "FC", "first class lounge" }, { "LG", "lounge" }, { "SHOP", "shop" }, { "CA", "cargo hold" },
            { "AP", "airport" }, { "LB", "lobby" }, { "CRD", "corridor" }, { "COURT", "courtroom" },
            { "GA", "park gate" }, { "WE", "Wild West area" }, { "ST", "stage" }, { "HO", "haunted house" }, { "HOP", "haunted house" },
            { "CON", "control room" }, { "HID", "hideout" }, { "CG", "underground passage" },
            { "ARM1", "apartment room 1" }, { "ARM2", "apartment room 2" },
            { "THE", "Theatrum Neutralis" }, { "OPA", "open-air stage" }, { "ROSE", "rose garden" },
            { "OVAL", "ambassador's office" }, { "FIRE", "room with the fireplace" },
        };

        private static Dictionary<string, string> _custom;
        private static string File => System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_rooms.txt");
        private static uint _room = uint.MaxValue;
        private static float _enteredAt = -1f;
        private static float _errAt = -100f;

        private static Dictionary<string, string> Custom()
        {
            if (_custom != null) return _custom;
            _custom = new Dictionary<string, string>();
            try
            {
                if (System.IO.File.Exists(File))
                    foreach (var l in System.IO.File.ReadAllLines(File))
                    {
                        int eq = l.IndexOf('=');
                        if (eq > 0) _custom[l.Substring(0, eq)] = l.Substring(eq + 1);
                    }
            }
            catch (Exception e) { Main.Log.Warning("Room names: " + e.Message); }
            return _custom;
        }

        private static uint CurrentMap(GK1::Il2Cpp.GKJ1 g)
        {
            try { var r = g.GetRecordData(); if (r?.mapinf_data != null) return r.mapinf_data.mymap * 1000 + r.mapinf_data.myarea; } catch { }
            return uint.MaxValue;
        }

        /// <summary>The room's name: yours if you named it, else a guess from the things in it.</summary>
        public static string Name(GK1::Il2Cpp.GKJ1 g)
        {
            uint room = CurrentMap(g);
            if (Custom().TryGetValue(room.ToString(), out var n)) return n;
            var count = new Dictionary<string, int>();
            try
            {
                var objs = g.GetMapObjData();
                for (int i = 0; objs != null && i < objs.Length; i++)
                {
                    var o = objs[i];
                    if (o == null || o.sts == 0) continue;
                    string raw = ObjNames.Raw(o.num);
                    int us = raw.IndexOf('_');
                    if (us <= 0) continue;
                    string code = raw.Substring(0, us);
                    if (code == "CHAR" || code.StartsWith("MZ") || code == "MAPOBJ") continue;
                    count[code] = (count.TryGetValue(code, out int c) ? c : 0) + 1;
                }
            }
            catch { }
            string best = null; int bestN = 0;
            foreach (var kv in count) if (kv.Value > bestN && Codes.ContainsKey(kv.Key)) { best = kv.Key; bestN = kv.Value; }
            Main.Log.Msg($"Room {room}: codes {string.Join(", ", count.Keys)}");
            return best != null ? Codes[best] : $"room {room / 1000}-{room % 1000}";
        }

        public static void Poll()
        {
            try
            {
                if (Gk2Map.InGame) return;   // second game: its rooms are read elsewhere
                var g = GK1::Il2Cpp.AppCore.gkj1;
                if (g == null || !Investigation.PlayerCanWalk) return;
                uint room = CurrentMap(g);
                if (room == uint.MaxValue || room == _room) return;
                bool first = _room == uint.MaxValue;
                _room = room;
                // Not when the game has just been loaded (you know where you are), only when you move on.
                if (first) return;
                Speech.Say(Name(g), false, false);
            }
            catch (Exception e) { if (Time.unscaledTime - _errAt > 10f) { _errAt = Time.unscaledTime; Main.Log.Warning("Rooms poll failed: " + e.Message.Split('\n')[0]); } }
        }

        /// <summary>F4: say the room's name.</summary>
        public static void Speak()
        {
            var g = GK1::Il2Cpp.AppCore.gkj1;
            if (g == null || !Investigation.InInvestigation) { Speech.Say("No room here.", true, false); return; }
            Speech.Say(Name(g), true, false);
        }

        /// <summary>Shift+F4: name this room with the text on the clipboard.</summary>
        public static void RenameFromClipboard()
        {
            var g = GK1::Il2Cpp.AppCore.gkj1;
            if (g == null || !Investigation.InInvestigation) { Speech.Say("No room here.", true, false); return; }
            string text = Clipboard()?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > 60 || text.Contains("\n")) { Speech.Say("Copy a short name first, then press Shift F4.", true, false); return; }
            uint room = CurrentMap(g);
            Custom()[room.ToString()] = text;
            try
            {
                var lines = new List<string>();
                foreach (var kv in Custom()) lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(File, lines);
            }
            catch (Exception e) { Main.Log.Warning("Couldn't save room names: " + e.Message); }
            Speech.Say("Room named " + text, true, false);
        }

        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);

        private static string Clipboard()
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                IntPtr h = GetClipboardData(13);   // CF_UNICODETEXT
                if (h == IntPtr.Zero) return null;
                IntPtr p = GlobalLock(h);
                try { return Marshal.PtrToStringUni(p); } finally { GlobalUnlock(h); }
            }
            finally { CloseClipboard(); }
        }
    }
}
