using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppGK_System.Controller;

namespace AAIAccess
{
    /// <summary>The game's keyboard layout as the player set it (Options, PC key config). The game's actions check its own
    /// buttons (ButtonType A = confirm, B = cancel ...); each button has one key, read from the game's keyboard device.
    /// Keys are Unity's Key values, which name a key by its place on a US keyboard, so the mod presses and watches them by
    /// scan code (the place), not by letter: right on French and German keyboards too.</summary>
    internal static class KeyMap
    {
        // The game's defaults (from its key map, 2026-11-11).
        private static readonly Dictionary<ButtonType, string> Default = new Dictionary<ButtonType, string>
        {
            { ButtonType.A, "Space" }, { ButtonType.B, "Z" }, { ButtonType.X, "T" }, { ButtonType.Y, "R" },
            { ButtonType.Start, "G" }, { ButtonType.Select, "O" }, { ButtonType.L1, "Q" }, { ButtonType.R1, "E" },
            { ButtonType.L3, "X" }, { ButtonType.R3, "C" },
            { ButtonType.DPAD_Up, "W" }, { ButtonType.DPAD_Left, "A" }, { ButtonType.DPAD_Down, "S" }, { ButtonType.DPAD_Right, "D" },
            { ButtonType.RS_Up, "UpArrow" }, { ButtonType.RS_Left, "LeftArrow" }, { ButtonType.RS_Down, "DownArrow" }, { ButtonType.RS_Right, "RightArrow" },
        };

        private static readonly Dictionary<ButtonType, string> _keys = new Dictionary<ButtonType, string>(Default);
        private static GKController _ctrl;
        private static float _nextRead, _nextFind;
        private static string _logged = "";

        /// <summary>Reads the layout again at most every 2 s.</summary>
        private static void Refresh()
        {
            float now = 0; try { now = UnityEngine.Time.unscaledTime; } catch { }
            if (now < _nextRead) return;
            _nextRead = now + 2f;
            try
            {
                if (_ctrl == null && now >= _nextFind) { _nextFind = now + 5f; _ctrl = UnityEngine.Object.FindObjectOfType<GKController>(); }
                var kb = _ctrl?.m_KeyboardController;
                if (kb == null) return;
                var sb = new System.Text.StringBuilder();
                foreach (var bt in Default.Keys)
                {
                    string k = kb.GetAssignKey(bt).ToString();
                    if (k == "None" || k.Length == 0) k = Default[bt];
                    _keys[bt] = k;
                    if (k != Default[bt]) sb.Append(bt).Append('=').Append(k).Append(' ');
                }
                string s = sb.ToString();
                if (s != _logged) { _logged = s; Main.Log.Msg(s.Length == 0 ? "Keyboard: the game's default keys" : "Keyboard keys changed from the default: " + s); }
            }
            catch (Exception e) { _ctrl = null; Main.Log.Warning("Keyboard layout: " + e.Message); }
        }

        /// <summary>The Unity key name for one of the game's buttons ("Space", "Z", "UpArrow" ...).</summary>
        public static string Key(ButtonType bt) { Refresh(); return _keys.TryGetValue(bt, out var k) ? k : ""; }

        /// <summary>The player is using a controller right now (the game's own idea of the current device).</summary>
        public static bool UsingController
        {
            get
            {
                Refresh();
                try { return _ctrl != null && (int)_ctrl.m_CurrentDeviceType >= (int)DeviceType.NintendoSwitch; }
                catch { return false; }
            }
        }

        /// <summary>True when every key the help names is the game's default.</summary>
        public static bool IsDefault
        {
            get { Refresh(); foreach (var kv in Default) if (_keys[kv.Key] != kv.Value) return false; return true; }
        }

        // ---------- names ----------

        /// <summary>The key's name for speech (English; Loc translates the common ones).</summary>
        public static string Name(ButtonType bt) => KeyName(Key(bt));

        public static string KeyName(string k)
        {
            if (k.Length == 1) return k;
            if (k.StartsWith("Digit")) return k.Substring(5);
            if (k.StartsWith("Numpad") && k.Length == 7) return "Numpad " + k.Substring(6);
            switch (k)
            {
                case "UpArrow": return "Up arrow"; case "DownArrow": return "Down arrow";
                case "LeftArrow": return "Left arrow"; case "RightArrow": return "Right arrow";
                case "LeftShift": return "Left Shift"; case "RightShift": return "Right Shift";
                case "LeftCtrl": return "Left Ctrl"; case "RightCtrl": return "Right Ctrl";
                case "LeftAlt": return "Left Alt"; case "RightAlt": return "Right Alt";
                case "PageUp": return "Page Up"; case "PageDown": return "Page Down"; case "CapsLock": return "Caps Lock";
                case "LeftBracket": return "Left bracket"; case "RightBracket": return "Right bracket";
                case "Backquote": return "Backquote"; case "Quote": return "Apostrophe";
                case "NumpadEnter": return "Numpad Enter"; case "NumpadPlus": return "Numpad plus"; case "NumpadMinus": return "Numpad minus";
                case "NumpadMultiply": return "Numpad multiply"; case "NumpadDivide": return "Numpad divide"; case "NumpadPeriod": return "Numpad period";
            }
            return k;   // Space, Enter, Tab, Backspace, Escape, Comma, Period, Slash, Backslash, Semicolon, Minus, Equals, F1 ...
        }

        /// <summary>Four movement keys as one phrase ("W A S D").</summary>
        public static string Move() => $"{Name(ButtonType.DPAD_Up)} {Name(ButtonType.DPAD_Left)} {Name(ButtonType.DPAD_Down)} {Name(ButtonType.DPAD_Right)}";

        /// <summary>The keys that turn evidence and look around: "the arrow keys" when they are the default.</summary>
        public static string Look()
        {
            if (Key(ButtonType.RS_Up) == "UpArrow" && Key(ButtonType.RS_Left) == "LeftArrow" && Key(ButtonType.RS_Down) == "DownArrow" && Key(ButtonType.RS_Right) == "RightArrow")
                return "the arrow keys";
            return $"{Name(ButtonType.RS_Up)} {Name(ButtonType.RS_Left)} {Name(ButtonType.RS_Down)} {Name(ButtonType.RS_Right)}";
        }

        // ---------- scan codes: pressing and watching keys ----------

        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        // Set-1 scan codes of keys by their place on a US keyboard (0xE0xx = extended key).
        private static readonly Dictionary<string, int> Scans = new Dictionary<string, int>
        {
            {"Escape",0x01},{"Digit1",0x02},{"Digit2",0x03},{"Digit3",0x04},{"Digit4",0x05},{"Digit5",0x06},{"Digit6",0x07},{"Digit7",0x08},{"Digit8",0x09},{"Digit9",0x0A},{"Digit0",0x0B},
            {"Minus",0x0C},{"Equals",0x0D},{"Backspace",0x0E},{"Tab",0x0F},
            {"Q",0x10},{"W",0x11},{"E",0x12},{"R",0x13},{"T",0x14},{"Y",0x15},{"U",0x16},{"I",0x17},{"O",0x18},{"P",0x19},{"LeftBracket",0x1A},{"RightBracket",0x1B},{"Enter",0x1C},
            {"LeftCtrl",0x1D},{"A",0x1E},{"S",0x1F},{"D",0x20},{"F",0x21},{"G",0x22},{"H",0x23},{"J",0x24},{"K",0x25},{"L",0x26},{"Semicolon",0x27},{"Quote",0x28},{"Backquote",0x29},
            {"LeftShift",0x2A},{"Backslash",0x2B},{"Z",0x2C},{"X",0x2D},{"C",0x2E},{"V",0x2F},{"B",0x30},{"N",0x31},{"M",0x32},{"Comma",0x33},{"Period",0x34},{"Slash",0x35},{"RightShift",0x36},
            {"NumpadMultiply",0x37},{"LeftAlt",0x38},{"Space",0x39},{"CapsLock",0x3A},
            {"F1",0x3B},{"F2",0x3C},{"F3",0x3D},{"F4",0x3E},{"F5",0x3F},{"F6",0x40},{"F7",0x41},{"F8",0x42},{"F9",0x43},{"F10",0x44},{"F11",0x57},{"F12",0x58},
            {"Numpad7",0x47},{"Numpad8",0x48},{"Numpad9",0x49},{"NumpadMinus",0x4A},{"Numpad4",0x4B},{"Numpad5",0x4C},{"Numpad6",0x4D},{"NumpadPlus",0x4E},
            {"Numpad1",0x4F},{"Numpad2",0x50},{"Numpad3",0x51},{"Numpad0",0x52},{"NumpadPeriod",0x53},
            {"NumpadEnter",0xE01C},{"RightCtrl",0xE01D},{"NumpadDivide",0xE035},{"RightAlt",0xE038},
            {"Home",0xE047},{"UpArrow",0xE048},{"PageUp",0xE049},{"LeftArrow",0xE04B},{"RightArrow",0xE04D},{"End",0xE04F},{"DownArrow",0xE050},{"PageDown",0xE051},{"Insert",0xE052},{"Delete",0xE053},
        };

        /// <summary>The scan code of a game button's key (0xE0xx for extended keys), or 0.</summary>
        public static int Scan(ButtonType bt) => Scans.TryGetValue(Key(bt), out var s) ? s : 0;

        /// <summary>The Windows virtual key for a scan code on the player's own keyboard layout.</summary>
        public static int VkOfScan(int scan) => scan == 0 ? 0 : (int)MapVirtualKey((uint)scan, 3);   // MAPVK_VSC_TO_VK_EX

        /// <summary>The key of one of the game's buttons is down right now.</summary>
        public static bool Down(ButtonType bt)
        {
            int vk = VkOfScan(Scan(bt));
            return vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
    }
}
