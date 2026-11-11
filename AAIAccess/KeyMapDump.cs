#if DEVTOOLS
using System;
using System.Text;
using Il2CppGK_System.Controller;
using Il2CppGK_System.Controller.Device;

namespace AAIAccess
{
    /// <summary>Developer build only: writes the game's button assignments to the log once, ~10 s after start.
    /// The game's actions check its own button names (ButtonType: A, B, X, Y, L1 ...); keyboard keys are mapped onto those
    /// (KeyboardDevice), and so are the controller's buttons. Knowing which key is which button tells us which controller
    /// button does what the help's keyboard keys do (E = Organizer, Q = Logic ...).</summary>
    internal static class KeyMapDump
    {
        private static bool _done;
        private static float _nextTry = 10f;
        private static int _tries;

        public static void Poll()
        {
            if (_done) return;
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextTry) return;
            _nextTry = now + 5f;
            if (++_tries > 12) { _done = true; Main.Log.Warning("Key map: gave up, no game controller object found."); return; }
            try { if (Dump()) _done = true; }
            catch (Exception e) { Main.Log.Warning("Key map: " + e.Message); }
        }

        private static GKController Controller()
        {
            // Only find the game's own object: Singleton.Instance might make a second one if asked too early.
            try { return UnityEngine.Object.FindObjectOfType<GKController>(); } catch { }
            return null;
        }

        private static bool Dump()
        {
            var c = Controller();
            if (c == null) return false;
            var sb = new StringBuilder();
            sb.Append("Key map: current device ");
            try { sb.Append(c.m_CurrentDeviceType); } catch (Exception e) { sb.Append("? " + e.Message); }
            Main.Log.Msg(sb.ToString());

            // Keyboard: the key the game uses for each of its buttons (your own key changes included).
            var kb = c.m_KeyboardController;
            if (kb == null) Main.Log.Msg("Key map: no keyboard device");
            else
            {
                sb.Clear().Append("Key map keyboard (game button = key): ");
                foreach (ButtonType bt in Enum.GetValues(typeof(ButtonType)))
                {
                    if (bt == ButtonType.None || bt == ButtonType.DefineMax) continue;
                    try { sb.Append(bt).Append('=').Append(kb.GetAssignKey(bt)).Append("; "); }
                    catch (Exception e) { sb.Append(bt).Append("=? (").Append(e.Message).Append("); "); }
                }
                Main.Log.Msg(sb.ToString());
                LogDict("Key map keyboard default", Get(() => kb.m_DefaultAssign));
                LogDict("Key map keyboard override", Get(() => kb.m_OverrideAssign));
            }

            // Controllers: which device classes exist, and any button reassignment they have.
            object devs = Get(() => c.m_DeviceControllers);
            if (devs == null) Main.Log.Msg("Key map: no controller devices");
            else
            {
                Main.Log.Msg("Key map devices: " + devs.GetType().Name);
                Each(devs, item =>
                {
                    object dev = item, key = null;
                    var kp = item?.GetType().GetProperty("Key");
                    var vp = item?.GetType().GetProperty("Value");
                    if (kp != null && vp != null) { key = kp.GetValue(item); dev = vp.GetValue(item); }
                    string name = key != null ? key + " -> " : "";
                    var il = dev as Il2CppSystem.Object;
                    XboxDevice x = null; PlayStationDevice ps = null; SwitchDevice sw = null; GamepadDevice gp = null;
                    try { x = il?.TryCast<XboxDevice>(); } catch { }
                    try { ps = il?.TryCast<PlayStationDevice>(); } catch { }
                    try { sw = il?.TryCast<SwitchDevice>(); } catch { }
                    try { gp = il?.TryCast<GamepadDevice>(); } catch { }
                    string kind = x != null ? "XboxDevice" : ps != null ? "PlayStationDevice" : sw != null ? "SwitchDevice" : gp != null ? "GamepadDevice" : (dev?.GetType().Name ?? "null");
                    Main.Log.Msg("Key map device: " + name + kind);
                    if (x != null) LogDict("Key map Xbox override", Get(() => x.m_OverrideAssign));
                });
            }

            // What the connected controller's buttons are called (Unity layout and display names).
            try
            {
                var g = UnityEngine.InputSystem.Gamepad.current;
                if (g == null) Main.Log.Msg("Key map: no controller connected");
                else
                {
                    sb.Clear().Append("Key map controller ").Append(g.layout).Append(" (").Append(g.displayName).Append("): ");
                    void N(string what, UnityEngine.InputSystem.InputControl ctl) { try { sb.Append(what).Append('=').Append(ctl.displayName).Append("; "); } catch { } }
                    N("south", g.buttonSouth); N("east", g.buttonEast); N("west", g.buttonWest); N("north", g.buttonNorth);
                    N("L1", g.leftShoulder); N("R1", g.rightShoulder); N("L2", g.leftTrigger); N("R2", g.rightTrigger);
                    N("start", g.startButton); N("select", g.selectButton);
                    Main.Log.Msg(sb.ToString());
                }
            }
            catch (Exception e) { Main.Log.Msg("Key map controller: " + e.Message); }
            Main.Log.Msg("Key map: done");
            return true;
        }

        private static object Get(Func<object> f) { try { return f(); } catch (Exception e) { Main.Log.Msg("Key map read failed: " + e.Message); return null; } }

        /// <summary>Goes through an Il2Cpp collection with its own enumerator.</summary>
        private static void Each(object coll, Action<object> a)
        {
            if (coll == null) return;
            var ge = coll.GetType().GetMethod("GetEnumerator", Type.EmptyTypes);
            if (ge == null) { Main.Log.Msg("Key map: can't go through " + coll.GetType().Name); return; }
            var en = ge.Invoke(coll, null);
            var mn = en.GetType().GetMethod("MoveNext");
            var cur = en.GetType().GetProperty("Current");
            int n = 0;
            while ((bool)mn.Invoke(en, null) && n++ < 200) a(cur.GetValue(en));
        }

        private static string Describe(object v)
        {
            if (v == null) return "null";
            if (v is UnityEngine.InputSystem.InputControl ic) { try { return ic.path; } catch { } }
            return v.ToString();
        }

        private static void LogDict(string title, object dict)
        {
            if (dict == null) { Main.Log.Msg(title + ": none"); return; }
            var sb = new StringBuilder(title).Append(": ");
            try
            {
                Each(dict, kv =>
                {
                    var t = kv.GetType();
                    var k = t.GetProperty("Key")?.GetValue(kv);
                    var v = t.GetProperty("Value")?.GetValue(kv);
                    sb.Append(k).Append('=').Append(Describe(v)).Append("; ");
                });
            }
            catch (Exception e) { sb.Append("(failed: ").Append((e.InnerException ?? e).Message).Append(')'); }
            Main.Log.Msg(sb.ToString());
        }
    }
}
#endif
