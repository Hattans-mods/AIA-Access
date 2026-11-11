using System;
using System.Runtime.InteropServices;
using Il2CppGK_System.Controller;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace AAIAccess
{
    /// <summary>Controller support for the mod keys. Hold the mod button (left stick button unless
    /// UserData/AAIAccess_pad.txt names another, or says "off") and press another button:
    /// Start = F1, bottom = F2, right = F3, left = H, top = F4,
    /// right trigger / D-pad right = ], left trigger / D-pad left = [, right shoulder / D-pad down = \ (walk), left shoulder / D-pad up = ' (track).
    /// L1 and R1 without the mod button are [ and ] on screens where the mod picks things (SelectScreen); there the game
    /// doesn't get them.
    /// While the mod button is held the game gets no controller buttons except the mod button itself, and a button pressed
    /// in that time stays hidden from the game until it is let go. Buttons are read by position, so this works the same on
    /// Xbox, PlayStation and Switch controllers.</summary>
    internal static class Pad
    {
        public enum B { South, East, West, North, Start, Select, L1, R1, L2, R2, L3, R3, Up, Down, Left, Right, Count }

        private static readonly bool[] _down = new bool[(int)B.Count];
        private static readonly bool[] _was = new bool[(int)B.Count];
        private static readonly bool[] _swallow = new bool[(int)B.Count];
        private static readonly bool[] _chord = new bool[(int)B.Count];
        private static readonly bool[] _solo = new bool[(int)B.Count];
        private static int _frame = -1;
        private static bool _connected, _modHeld, _selectOn;

        /// <summary>Set by Main: true on screens where L1 and R1 alone pick the previous / next thing.</summary>
        public static Func<bool> SelectScreen;
        private static float _stickX, _stickY;

        private static B _mod = B.L3;
        public static bool Enabled { get; private set; } = true;

        /// <summary>The kind of controller, for naming its buttons the way they are printed on it.</summary>
        public enum Family { Generic, Xbox, PlayStation, DualSense, Switch }
        public static Family Kind { get; private set; }
        private static int _deviceId = -1;
        private static Family? _forced;        // set in AAIAccess_pad.txt
        private static Family _unityKind;      // what Unity says the controller is
        private static float _nextScan;

        // Where the game's A, B, X, Y (ButtonType 0-3) sit on the controller. The game's X (partner, present; key T) is the
        // top button and its Y (history; key R) the left one, as the author found with a DualSense through Steam Input
        // (Triangle and Square; Y and X on an Xbox controller). Corrected from which game button a face button sets the
        // first time it is pressed alone.
        private static readonly B[] DefaultFace = { B.South, B.East, B.North, B.West };
        private static readonly B[] _faceOf = (B[])DefaultFace.Clone();

        /// <summary>Reads UserData/AAIAccess_pad.txt and hooks the game's controller reading. The file holds words in any
        /// order: the mod button (L3, R3, Select, Start, L1, R1, L2, R2), or off; and optionally which controller it is,
        /// for its button names (ps5, ps4, xbox, switch, other) when the mod can't tell by itself.</summary>
        public static void Init(HarmonyLib.Harmony h)
        {
            try
            {
                string f = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_pad.txt");
                if (System.IO.File.Exists(f))
                {
                    foreach (var word in System.IO.File.ReadAllText(f).ToUpperInvariant().Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        switch (word)
                        {
                            case "OFF": Enabled = false; Main.Log.Msg("Controller mod keys off (AAIAccess_pad.txt)."); return;
                            case "SELECT": case "BACK": case "VIEW": case "SHARE": case "CREATE": case "MINUS": _mod = B.Select; break;
                            case "START": case "MENU": case "OPTIONS": case "PLUS": _mod = B.Start; break;
                            case "L3": case "LS": _mod = B.L3; break;
                            case "R3": case "RS": _mod = B.R3; break;
                            case "L1": case "LB": _mod = B.L1; break;
                            case "R1": case "RB": _mod = B.R1; break;
                            case "L2": case "LT": _mod = B.L2; break;
                            case "R2": case "RT": _mod = B.R2; break;
                            case "PS5": case "DUALSENSE": _forced = Family.DualSense; break;
                            case "PS4": case "PS": case "PLAYSTATION": case "DUALSHOCK": _forced = Family.PlayStation; break;
                            case "XBOX": _forced = Family.Xbox; break;
                            case "SWITCH": case "NINTENDO": _forced = Family.Switch; break;
                            case "OTHER": case "GENERIC": _forced = Family.Generic; break;
                            case "NOSTEAM": _steamOff = true; Main.Log.Msg("Not asking Steam Input which controller it is (AAIAccess_pad.txt)."); break;
                            default: Main.Log.Warning("AAIAccess_pad.txt: unknown word '" + word + "'"); break;
                        }
                    }
                }
            }
            catch (Exception e) { Main.Log.Warning("Controller settings: " + e.Message); }
            Main.Log.Msg("Controller mod button: " + _mod + (_forced.HasValue ? ", controller set to " + _forced.Value : ""));

            // Hide buttons from the game while the mod button is held. SwitchDevice uses GamepadDevice's Update.
            var post = new HarmonyLib.HarmonyMethod(typeof(Pad), nameof(AfterDeviceUpdate));
            foreach (var t in new[] { typeof(Il2CppGK_System.Controller.Device.GamepadDevice), typeof(Il2CppGK_System.Controller.Device.XboxDevice), typeof(Il2CppGK_System.Controller.Device.PlayStationDevice) })
            {
                try
                {
                    var m = HarmonyLib.AccessTools.DeclaredMethod(t, "Update");
                    if (m == null) { Main.Log.Warning("No Update on " + t.Name); continue; }
                    h.Patch(m, postfix: post);
                }
                catch (Exception e) { Main.Log.Warning($"Controller hook on {t.Name} failed: {e.Message}"); }
            }
        }

        private static bool Read(ButtonControl c) { try { return c != null && c.isPressed; } catch { return false; } }

        /// <summary>Reads the controller once per frame.</summary>
        private static void Refresh()
        {
            int f = UnityEngine.Time.frameCount;
            if (f == _frame) return;
            _frame = f;
            Array.Copy(_down, _was, _down.Length);
            Array.Clear(_down, 0, _down.Length);
            _stickX = _stickY = 0;
            Gamepad g = null;
            try { g = Gamepad.current; } catch { }
            _connected = g != null;
            if (g != null)
            {
                int id = -1; try { id = g.deviceId; } catch { }
                if (id != _deviceId) { _deviceId = id; Detect(g); }
                else if (!_forced.HasValue && (_unityKind == Family.Xbox || _unityKind == Family.Generic))
                {
                    // Behind Steam Input every controller looks like an Xbox one: look again now and then for the real one.
                    float now = UnityEngine.Time.unscaledTime;
                    // Steam answers "unknown" for the first seconds: look every 2 s in the first minute, then every 10 s.
                    if (now >= _nextScan) { _nextScan = now + (now < 60f ? 2f : 10f); SetKind(SteamKind() ?? Physical() ?? _unityKind, false); }
                }
                _down[(int)B.South] = Read(g.buttonSouth);
                _down[(int)B.East] = Read(g.buttonEast);
                _down[(int)B.West] = Read(g.buttonWest);
                _down[(int)B.North] = Read(g.buttonNorth);
                _down[(int)B.Start] = Read(g.startButton);
                _down[(int)B.Select] = Read(g.selectButton);
                _down[(int)B.L1] = Read(g.leftShoulder);
                _down[(int)B.R1] = Read(g.rightShoulder);
                _down[(int)B.L2] = Read(g.leftTrigger);
                _down[(int)B.R2] = Read(g.rightTrigger);
                _down[(int)B.L3] = Read(g.leftStickButton);
                _down[(int)B.R3] = Read(g.rightStickButton);
                try
                {
                    var d = g.dpad;
                    _down[(int)B.Up] = Read(d.up); _down[(int)B.Down] = Read(d.down);
                    _down[(int)B.Left] = Read(d.left); _down[(int)B.Right] = Read(d.right);
                }
                catch { }
                try { var v = g.leftStick.ReadValue(); _stickX = v.x; _stickY = v.y; } catch { }
            }
            _modHeld = Enabled && _down[(int)_mod];
            _selectOn = false;
            if (Enabled && _connected && !_modHeld && (_down[(int)B.L1] || _down[(int)B.R1]))
                try { _selectOn = SelectScreen != null && SelectScreen(); } catch { }
            for (int i = 0; i < _down.Length; i++)
            {
                bool newPress = _down[i] && !_was[i];
                _chord[i] = _modHeld && i != (int)_mod && newPress;
                _solo[i] = _selectOn && newPress && (i == (int)B.L1 || i == (int)B.R1) && i != (int)_mod;
                if (!_down[i]) _swallow[i] = false;
                else if ((_modHeld && i != (int)_mod) || _solo[i]) _swallow[i] = true;
            }
        }

        private static bool _stickOut;

        /// <summary>Call once per frame before asking Chord. A controller press stops speech the way a key press does
        /// with a screen reader; it runs before anything the press makes the mod say.</summary>
        public static void Poll()
        {
            try
            {
                Refresh();
                if (!Enabled || !_connected) return;
                bool pressed = false;
                for (int i = 0; i < _down.Length; i++) if (_down[i] && !_was[i]) { pressed = true; break; }
                float m = Math.Max(Math.Abs(_stickX), Math.Abs(_stickY));
                bool out_ = _stickOut ? m > 0.3f : m > 0.5f;   // a little dead zone each way, so a resting stick doesn't flicker
                if (out_ && !_stickOut) pressed = true;
                _stickOut = out_;
                if (pressed && Main.GameHasFocus) Speech.UserInterrupt();
            }
            catch { }
        }

        /// <summary>True once when this button is pressed while the mod button is held.</summary>
        public static bool Chord(B b) => Enabled && _chord[(int)b];

        /// <summary>True once when L1 or R1 is pressed on its own on a screen where it picks things.</summary>
        public static bool Solo(B b) => Enabled && _solo[(int)b];

        /// <summary>A controller is connected.</summary>
        public static bool Connected { get { try { Refresh(); } catch { } return _connected; } }

        /// <summary>The bottom button (the game's confirm) is down and not hidden from the game.</summary>
        public static bool ConfirmDown { get { try { Refresh(); } catch { } return _down[(int)B.South] && !_swallow[(int)B.South]; } }

        /// <summary>The player is moving with the left stick or the D-pad (not counting a mod chord).</summary>
        public static bool MoveHeld
        {
            get
            {
                try { Refresh(); } catch { }
                if (_modHeld) return false;
                if (Math.Abs(_stickX) > 0.3f || Math.Abs(_stickY) > 0.3f) return true;
                for (var b = B.Up; b <= B.Right; b++) if (_down[(int)b] && !_swallow[(int)b]) return true;
                return false;
            }
        }

        // Game ButtonType for each of ours (the game's own A/B/X/Y follow the device; we block them all anyway).
        private static readonly ButtonType[] AllGameButtons =
        {
            ButtonType.A, ButtonType.B, ButtonType.X, ButtonType.Y, ButtonType.Start, ButtonType.Select,
            ButtonType.L1, ButtonType.R1, ButtonType.L2, ButtonType.R2, ButtonType.L3, ButtonType.R3,
            ButtonType.DPAD_Right, ButtonType.DPAD_Left, ButtonType.DPAD_Up, ButtonType.DPAD_Down,
            ButtonType.RS_Right, ButtonType.RS_Left, ButtonType.RS_Up, ButtonType.RS_Down,
        };
        private static ButtonType GameButton(B b) => b switch
        {
            B.Start => ButtonType.Start, B.Select => ButtonType.Select,
            B.L1 => ButtonType.L1, B.R1 => ButtonType.R1, B.L2 => ButtonType.L2, B.R2 => ButtonType.R2,
            B.L3 => ButtonType.L3, B.R3 => ButtonType.R3, _ => ButtonType.None,
        };

        private static bool Blocking()
        {
            if (_modHeld) return true;
            for (int i = 0; i < _swallow.Length; i++) if (_swallow[i]) return true;
            return false;
        }

        private static float _lastErrAt = -100f;

        /// <summary>After the game reads the controller: while the mod button is held (or a button pressed with it is
        /// still down), the game sees no buttons except the mod button.</summary>
        private static void AfterDeviceUpdate(InputStatus inputStatus)
        {
            try
            {
                if (!Enabled) return;
                Refresh();
                var st = inputStatus;
                if (st == null) return;
                var states = st.ButtonStates;
                if (states == null) return;
                Learn(states);
                if (!Blocking()) return;
                // Only L1 / R1 used on their own: hide just those. Otherwise (a chord) hide everything but the mod button.
                bool all = _modHeld;
                for (int i = 0; i < _swallow.Length; i++) if (_swallow[i] && i != (int)B.L1 && i != (int)B.R1) all = true;
                if (all)
                {
                    ButtonType keep = _modHeld ? GameButton(_mod) : ButtonType.None;
                    foreach (var bt in AllGameButtons)
                    {
                        if (bt == keep) continue;
                        int i = (int)bt;
                        if (i < states.Length) states[i] = ButtonState.None;
                    }
                }
                else
                {
                    if (_swallow[(int)B.L1] && (int)ButtonType.L1 < states.Length) states[(int)ButtonType.L1] = ButtonState.None;
                    if (_swallow[(int)B.R1] && (int)ButtonType.R1 < states.Length) states[(int)ButtonType.R1] = ButtonState.None;
                }
                if (_modHeld) st.LeftStick = UnityEngine.Vector2.zero;
                st.UpdateBit();
            }
            catch (Exception e)
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now - _lastErrAt > 10f) { _lastErrAt = now; Main.Log.Warning("Controller block: " + e.Message); }
            }
        }

        // ---------- what the buttons are called ----------

        private static void Detect(Gamepad g)
        {
            Family k = Family.Generic;
            try
            {
                if (g.TryCast<UnityEngine.InputSystem.DualShock.DualSenseGamepadHID>() != null) k = Family.DualSense;
                else if (g.TryCast<UnityEngine.InputSystem.DualShock.DualShockGamepad>() != null) k = Family.PlayStation;
                else if (g.TryCast<UnityEngine.InputSystem.Switch.SwitchProControllerHID>() != null) k = Family.Switch;
                else if (g.TryCast<UnityEngine.InputSystem.XInput.XInputController>() != null) k = Family.Xbox;
            }
            catch { }
            _unityKind = k;
            string layout = ""; try { layout = g.layout; } catch { }
            Main.Log.Msg($"Controller seen by the game: {k} ({layout})");
            Family kind = k;
            if (_forced.HasValue) kind = _forced.Value;
            else if (k == Family.Xbox || k == Family.Generic) { kind = SteamKind() ?? Physical() ?? k; _nextScan = UnityEngine.Time.unscaledTime + 2f; }
            SetKind(kind, true);
        }

        private static void SetKind(Family k, bool newDevice)
        {
            if (k == Kind && !newDevice) return;
            if (k != Kind) Array.Copy(DefaultFace, _faceOf, 4);
            bool changed = k != Kind;
            Kind = k;
            if (changed || newDevice)
                Main.Log.Msg($"Controller buttons named for: {k}" + (_forced.HasValue ? " (AAIAccess_pad.txt)" : k != _unityKind ? " (the real controller, behind Steam Input)" : ""));
        }

        // ---------- the real controller behind Steam Input: ask Steam ----------
        // Steam hides the real controller from the game's process, so Windows' device list (below) can't see it there.
        // Steam itself says what is in each Xbox slot it emulates (Steamworks.NET, which the game ships).

        private static bool _steamInit, _steamOff;
        private static int _steamFails;
        private static string _steamSeen = "";

        private static Family? SteamKind()
        {
            if (_steamOff) return null;
            try
            {
                if (!Il2CppSteamworks.SteamAPI.IsSteamRunning()) return null;
                if (!_steamInit)
                {
                    _steamInit = Il2CppSteamworks.SteamInput.Init(false);
                    if (!_steamInit) { if (++_steamFails >= 6) { _steamOff = true; Main.Log.Msg("Steam Input: not available."); } return null; }
                    Main.Log.Msg("Steam Input: asking which controller it is.");
                }
                for (int i = 0; i < 4; i++)
                {
                    var h = Il2CppSteamworks.SteamInput.GetControllerForGamepadIndex(i);
                    if (h.m_InputHandle == 0) continue;
                    var t = Il2CppSteamworks.SteamInput.GetInputTypeForHandle(h);
                    string seen = $"slot {i}: {t}";
                    if (seen != _steamSeen) { _steamSeen = seen; Main.Log.Msg("Steam Input controller " + seen); }
                    switch (t)
                    {
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_PS5Controller: return Family.DualSense;
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_PS4Controller:
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_PS3Controller: return Family.PlayStation;
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_SwitchProController:
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_SwitchJoyConPair: return Family.Switch;
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_XBox360Controller:
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_XBoxOneController:
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_SteamDeckController:
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_SteamController: return Family.Xbox;
                        case Il2CppSteamworks.ESteamInputType.k_ESteamInputType_Unknown: continue;
                        default: return Family.Generic;
                    }
                }
            }
            catch (Exception e)
            {
                // Steam may not be started by the game yet: try again later, a few times.
                if (++_steamFails >= 6) { _steamOff = true; Main.Log.Msg("Steam Input: can't ask (" + e.Message + ")"); }
            }
            return null;
        }

        // ---------- the real controller in Windows' device list (when Steam doesn't hide it) ----------
        // Steam Input hands the game a virtual Xbox controller, but Windows still lists the real one as a HID device:
        // Sony's maker id is 054C (PS5 DualSense 0CE6, DualSense Edge 0DF2), Nintendo's 057E (Switch Pro 2009).

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid classGuid, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        private static readonly System.Text.RegularExpressions.Regex VidPid = new System.Text.RegularExpressions.Regex(
            @"vid[_&](?:0002)?([0-9a-f]{4}).*?pid[_&]([0-9a-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static string _lastSeen = "";

        /// <summary>A Sony or Nintendo controller plugged in or paired right now, or null.</summary>
        private static Family? Physical()
        {
            Family? found = null;
            string seen = "";
            IntPtr set = new IntPtr(-1);
            try
            {
                var hid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
                set = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, 0x02 | 0x10);   // present, device interfaces
                if (set == new IntPtr(-1)) return null;
                IntPtr buf = Marshal.AllocHGlobal(2048);
                try
                {
                    for (int i = 0; i < 256; i++)
                    {
                        var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                        if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref d)) break;
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref d, buf, 2048, out _, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(buf + 4) ?? "";
                        var m = VidPid.Match(path);
                        if (!m.Success) continue;
                        string vid = m.Groups[1].Value.ToUpperInvariant(), pid = m.Groups[2].Value.ToUpperInvariant();
                        if (vid == "054C")
                        {
                            var k = pid == "0CE6" || pid == "0DF2" ? Family.DualSense : Family.PlayStation;
                            if (found != Family.DualSense) found = k;
                            seen = $"Sony {pid}";
                        }
                        else if (vid == "057E" && found == null && (pid == "2009" || pid == "2006" || pid == "2007"))
                        { found = Family.Switch; seen = $"Nintendo {pid}"; }
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch (Exception e) { Main.Log.Warning("Controller search: " + e.Message); }
            finally { if (set != new IntPtr(-1)) SetupDiDestroyDeviceInfoList(set); }
            if (seen != _lastSeen) { _lastSeen = seen; if (seen.Length > 0) Main.Log.Msg("Controller found in Windows: " + seen); }
            return found;
        }

        /// <summary>One face button pressed alone: note which of the game's A, B, X, Y it set.</summary>
        private static void Learn(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ButtonState> states)
        {
            int pos = -1, n = 0;
            for (int p = (int)B.South; p <= (int)B.North; p++) if (_down[p] && !_was[p]) { pos = p; n++; }
            if (n != 1) return;
            int hit = -1, m = 0;
            for (int bt = 0; bt < 4 && bt < states.Length; bt++) if (states[bt] == ButtonState.Trigger) { hit = bt; m++; }
            if (m != 1 || _faceOf[hit] == (B)pos) return;
            for (int bt = 0; bt < 4; bt++) if (_faceOf[bt] == (B)pos) _faceOf[bt] = _faceOf[hit];   // keep it one-to-one
            _faceOf[hit] = (B)pos;
            Main.Log.Msg($"Controller: game button {(ButtonType)hit} is the {(B)pos} button");
        }

        /// <summary>What this button is called on the connected controller (English; Loc translates it).</summary>
        public static string Name(B b)
        {
            switch (Kind)
            {
                case Family.Xbox:
                    switch (b)
                    {
                        case B.South: return "A"; case B.East: return "B"; case B.West: return "X"; case B.North: return "Y";
                        case B.L1: return "LB"; case B.R1: return "RB"; case B.L2: return "LT"; case B.R2: return "RT";
                        case B.Start: return "the Menu button"; case B.Select: return "the View button";
                    }
                    break;
                case Family.PlayStation:
                case Family.DualSense:
                    switch (b)
                    {
                        case B.South: return "Cross"; case B.East: return "Circle"; case B.West: return "Square"; case B.North: return "Triangle";
                        case B.L1: return "L1"; case B.R1: return "R1"; case B.L2: return "L2"; case B.R2: return "R2";
                        case B.L3: return "L3"; case B.R3: return "R3";
                        case B.Start: return "Options"; case B.Select: return Kind == Family.DualSense ? "Create" : "Share";
                    }
                    break;
                case Family.Switch:
                    switch (b)
                    {
                        case B.South: return "B"; case B.East: return "A"; case B.West: return "Y"; case B.North: return "X";
                        case B.L1: return "L"; case B.R1: return "R"; case B.L2: return "ZL"; case B.R2: return "ZR";
                        case B.Start: return "Plus"; case B.Select: return "Minus";
                    }
                    break;
            }
            switch (b)
            {
                case B.South: return "the bottom button"; case B.East: return "the right button";
                case B.West: return "the left button"; case B.North: return "the top button";
                case B.L1: return "the left shoulder button"; case B.R1: return "the right shoulder button";
                case B.L2: return "the left trigger"; case B.R2: return "the right trigger";
                case B.L3: return "the left stick button"; case B.R3: return "the right stick button";
                case B.Start: return "Start"; case B.Select: return "Select";
            }
            return b.ToString();
        }

        /// <summary>The name of the controller button that does the game's button bt (A = confirm, B = cancel ...).</summary>
        public static string Name(ButtonType bt)
        {
            int i = (int)bt;
            if (i >= 0 && i < 4) return Name(_faceOf[i]);
            return bt switch
            {
                ButtonType.L1 => Name(B.L1), ButtonType.R1 => Name(B.R1), ButtonType.L2 => Name(B.L2), ButtonType.R2 => Name(B.R2),
                ButtonType.L3 => Name(B.L3), ButtonType.R3 => Name(B.R3), ButtonType.Start => Name(B.Start), ButtonType.Select => Name(B.Select),
                _ => bt.ToString(),
            };
        }

        /// <summary>The mod button's name.</summary>
        public static string ModName => Name(_mod);

        /// <summary>A help sentence with button names filled in. The template must be a line of lang/en.txt as it is.</summary>
        public static string F(string template, params string[] names) => string.Format(template, names);

        /// <summary>The controller's mod keys for help ("" with no controller). full: also next / previous / walk / track
        /// (left out where the screen's own help already says them).</summary>
        public static string Help(bool full)
        {
            if (!Enabled || !Connected) return "";
            string s = F("With a controller, hold {0} and press {1} for help, {2} to repeat, {3} for the puzzle status.", ModName, Name(B.Start), Name(B.South), Name(B.East)) + " " +
                       F("Still holding it, press {0} for the truth gauge and {1} for the room name.", Name(B.West), Name(B.North));
            if (full)
                s += " " + F("Press {0} on its own for the next person, thing or spot, and {1} for the previous one.", Name(B.R1), Name(B.L1)) +
                     " " + F("Hold {0} and press {1} to walk there by itself and {2} to track it with a sound; do it again to stop.", ModName, Name(B.R1), Name(B.L1));
            return s + " " + F("While holding {0}, you can also press {1} for the next one and {2} for the previous one, or use the D-pad: right next, left previous, down walk, up track.", ModName, Name(B.R2), Name(B.L2));
        }
    }
}
