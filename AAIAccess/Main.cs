using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using Il2CppGK_Common.UI.Message;

[assembly: MelonInfo(typeof(AAIAccess.Main), "AAI Access", "0.1.0", "AAI Access contributors")]
[assembly: MelonGame("CAPCOM Co., Ltd", "GK12")]

namespace AAIAccess
{
    public class Main : MelonMod
    {
        internal static MelonLogger.Instance Log;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Log.Msg("AAI Access loaded.");
#if DEVTOOLS
            CrashDump.Install();
            FileSplit.Start();
#endif
            // Test switch: UserData/AAIAccess_off.txt containing "off" turns the whole mod off (to tell game bugs from mod bugs).
            try
            {
                string f = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_off.txt");
                string sw = System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f).Trim().ToLowerInvariant() : "";
#if DEVTOOLS
                // "nodetail": everything except what reads evidence pages (to find what crashes on turning a text page).
                if (sw.StartsWith("nodetail"))
                {
                    NoDetail = true;
                    try
                    {
                        var m = HarmonyLib.AccessTools.Method(typeof(Il2CppGK_Common.UI.Sousa.GKSousaTextCut), "Setup");
                        if (m != null) HarmonyInstance.Unpatch(m, HarmonyLib.HarmonyPatchType.Postfix, HarmonyInstance.Id);
                    }
                    catch (System.Exception e) { Log.Warning("Unpatch text cut: " + e.Message); }
                    Log.Msg("AAI Access: evidence page reading switched OFF for a test (AAIAccess_off.txt says nodetail).");
                    return;
                }
                // "listonly": no hooks into the game at all; only the evidence list is read (crash test).
                if (sw.StartsWith("listonly"))
                {
                    ListOnly = true;
                    HarmonyInstance.UnpatchSelf();
                    Log.Msg("AAI Access test mode: only the evidence list is read, no hooks (AAIAccess_off.txt says listonly).");
                    return;
                }
#endif
                if (sw.StartsWith("off"))
                {
                    Off = true;
                    HarmonyInstance.UnpatchSelf();
                    Log.Msg("AAI Access is switched OFF (AAIAccess_off.txt): no hooks, no speech.");
                    return;
                }
            }
            catch (System.Exception e) { Log.Warning("Off switch: " + e.Message); }
            DetailPatches.Apply(HarmonyInstance);
        }

        // MelonLoader hooks il2cpp_resolve_icall to add missing Unity internal calls. In this game that hook
        // runs into endless recursion (stack overflow) when a text evidence page is turned, so we take it out.
        // The game itself never needs it. Put "icall" in UserData/AAIAccess_icall.txt to keep it (for testing).
        private static void StopICallInjector()
        {
            try
            {
                string keep = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_icall.txt");
                if (System.IO.File.Exists(keep)) { Log.Msg("ICall injector kept (AAIAccess_icall.txt)."); return; }
                var t = typeof(MelonLoader.MelonMod).Assembly.GetType("MelonLoader.Fixes.Il2CppInterop.Il2CppICallInjector");
                var m = t?.GetMethod("Shutdown", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (m == null) { Log.Msg("ICall injector not found; nothing to stop."); return; }
                m.Invoke(null, null);
                Log.Msg("ICall injector stopped.");
            }
            catch (System.Exception e) { Log.Warning("Stopping ICall injector failed: " + e.Message); }
        }

#if DEVTOOLS
        // Crash test: take out Il2CppInterop's native hooks named in UserData/AAIAccess_unhook.txt
        // (field names of InjectorHelpers, or "all"). Logs what it did.
        private static void UnhookInterop()
        {
            try
            {
                string f = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_unhook.txt");
                if (!System.IO.File.Exists(f)) return;
                string want = System.IO.File.ReadAllText(f).Trim();
                if (want.Length == 0) return;
                var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                var BI = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                var ia = typeof(Il2CppInterop.Runtime.IL2CPP).Assembly;
                var ih = ia.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers");
                var ml = typeof(MelonLoader.MelonMod).Assembly;
                var fixer = ml.GetType("MelonLoader.CoreClrUtils.CoreClrDelegateFixer")?.GetMethod("GetFixedPointerForDelegate", BF);
                var detach = ml.GetType("MelonLoader.InternalUtils.BootstrapInterop")?.GetMethod("NativeHookDetachDirect", BF);
                if (ih == null || fixer == null || detach == null) { Log.Warning("Unhook: helpers not found"); return; }
                string[] names = { "GenericMethodGetMethodHook", "GenericMethodGetMethodHook_Unity6", "GetTypeInfoFromTypeDefinitionIndexHook", "GetFieldDefaultValueHook", "FromIl2CppTypeHook", "FromNameHook" };
                foreach (var n in names)
                {
                    if (!want.Equals("all", System.StringComparison.OrdinalIgnoreCase) && want.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        object hook = ih.GetField(n, BF)?.GetValue(null);
                        if (hook == null) { Log.Msg("Unhook " + n + ": no such hook"); continue; }
                        System.Reflection.FieldInfo Fld(string name)
                        {
                            for (var t = hook.GetType(); t != null; t = t.BaseType)
                            { var fi = t.GetField(name, BI | System.Reflection.BindingFlags.DeclaredOnly); if (fi != null) return fi; }
                            return null;
                        }
                        var fApplied = Fld("_isApplied");
                        if (!(bool)fApplied.GetValue(hook)) { Log.Msg("Unhook " + n + ": not applied"); continue; }
                        var method = (System.Delegate)Fld("_method").GetValue(hook);
                        var det = (System.Delegate)Fld("_detour").GetValue(hook);
                        System.IntPtr target = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(method);
                        System.IntPtr detPtr = (System.IntPtr)fixer.Invoke(null, new object[] { det });
                        System.IntPtr cell = System.Runtime.InteropServices.Marshal.AllocHGlobal(8);
                        System.Runtime.InteropServices.Marshal.WriteIntPtr(cell, target);
                        detach.Invoke(null, new object[] { (nint)cell, (nint)detPtr });
                        fApplied.SetValue(hook, false);
                        Log.Msg($"Unhooked {n}: target 0x{target.ToInt64():X} detour 0x{detPtr.ToInt64():X}");
                    }
                    catch (System.Exception e) { Log.Warning("Unhook " + n + " failed: " + (e.InnerException ?? e).Message); }
                }
            }
            catch (System.Exception e) { Log.Warning("Unhook failed: " + e.Message); }
        }
#endif

        private readonly bool[] _keyDown = new bool[256];

        public static bool Off, NoDetail, ListOnly;
        private static bool _detailWas;

        public override void OnUpdate()
        {
            if (Off) return;
            if (ListOnly)
            {
                try { Organizer.Poll(); } catch { }
                try { bool d = EvidenceDetail.PageOpen(); if (d != _detailWas) { _detailWas = d; Log.Msg(d ? "Evidence page open" : "Evidence page closed"); if (d) Speech.Say("Page open", true, false); } } catch { }
                return;
            }
            try { Warmup.Poll(); } catch (Exception e) { Log.Warning("Warm-up: " + e.Message); }
            if (Pressed(0x70)) // F1: help for the current screen
                Help.Speak();
            if (Pressed(0x71)) // F2: repeat last spoken line
                Speech.Say(string.IsNullOrEmpty(Speech.LastSpoken) ? "Nothing to repeat yet." : Speech.LastSpoken, true, false);
            if (Pressed(0x72)) // F3: status of the current puzzle
            {
                if (EvidenceDetail.Active) EvidenceDetail.ReadStatus();
                else if (Organizer.Active) Organizer.ReadStatus();
                else if (LogicScreen.Active) LogicScreen.ReadStatus();
                else if (ExamineScreen.Active) ExamineScreen.ReadStatus();
                else if (Gk2Video.Active) Gk2Video.ReadStatus();
                else if (Gk2Luminol.Active) Gk2Luminol.ReadStatus();
                else if (Gk2Finger.Active) Gk2Finger.ReadStatus();
                else if (Gk2Item2D.Active) Gk2Item2D.ReadStatus();
                else if (Gk2Examine.Active) Gk2Examine.ReadStatus();
                else if (Gk2Examine3D.Active) Gk2Examine3D.ReadStatus();
                else if (Examine3D.Active) Examine3D.ReadStatus();
                else Speech.Say("Nothing to report here.", true, false);
            }
            if (Pressed(0x48)) { if (!LogicChess.SpeakTime() && !Gk2Gauge.Speak()) Gauge.Speak(); }   // H: truth gauge
            if (Pressed(0x73))   // F4: room name; Shift+F4: rename the room from the clipboard
            {
                if ((GetAsyncKeyState(0x10) & 0x8000) != 0) Rooms.RenameFromClipboard(); else Rooms.Speak();
            }
            if (Pressed(0xDB)) { if (Gk2Video.Active) Gk2Video.Step(-1); else if (Gk2Luminol.Active) Gk2Luminol.Step(-1); else if (Gk2Finger.Active) Gk2Finger.Step(-1); else if (ExamineScreen.Active) ExamineScreen.Step(-1); else if (Gk2Examine.Active) Gk2Examine.Step(-1); else if (Gk2Examine3D.Active) Gk2Examine3D.Step(-1); else if (Examine3D.Active) Examine3D.Step(-1); else if (Gk2Nav.Active) Gk2Nav.Step(-1); else Navigator.Step(-1); }   // [ previous
            if (Pressed(0xDD)) { if (Gk2Video.Active) Gk2Video.Step(+1); else if (Gk2Luminol.Active) Gk2Luminol.Step(+1); else if (Gk2Finger.Active) Gk2Finger.Step(+1); else if (ExamineScreen.Active) ExamineScreen.Step(+1); else if (Gk2Examine.Active) Gk2Examine.Step(+1); else if (Gk2Examine3D.Active) Gk2Examine3D.Step(+1); else if (Examine3D.Active) Examine3D.Step(+1); else if (Gk2Nav.Active) Gk2Nav.Step(+1); else Navigator.Step(+1); }   // ] next
            if (Pressed(0xDC) && !ExamineScreen.Active && !Gk2Examine.Active && !Gk2Examine3D.Active && !Gk2Video.Active && !Gk2Luminol.Active && !Gk2Finger.Active) { if (Gk2Nav.Active) Gk2Nav.Walk(); else Navigator.WalkToSelected(); }
            if (Pressed(0xDE) && !ExamineScreen.Active && !Gk2Examine.Active && !Gk2Examine3D.Active && !Gk2Video.Active && !Gk2Luminol.Active && !Gk2Finger.Active) { if (Gk2Nav.Active) Gk2Nav.Ping(); else Navigator.Ping(); }   // ' sound from where it is   // \ walk to it
#if DEVTOOLS
            if (Pressed(0x78)) PageExport.Start();   // F9: export evidence page pictures (for development)
            if (Pressed(0x79)) // F10: write map positions to the log (for development)
                { if (!Gk2Map.Dump()) Investigation.DumpMap(); }
            if (Pressed(0x7A)) // F11: write everything on screen to the log (for development)
                DebugDump.Dump();
#endif

            // While an evidence page is open, only what reads it runs: turning text pages crashed the game, and
            // nothing else is needed there.
            bool detail = false;
            try { detail = EvidenceDetail.PageOpen(); } catch { }
            if (detail != _detailWas) { _detailWas = detail; Log.Msg(detail ? "Evidence page open: other checks paused" : "Evidence page closed: checks resumed"); }
            T("Menus", Menus.Poll);
            T("TextCards", TextCards.Poll);
            T("Organizer", Organizer.Poll);
            T("EvidenceDetail", EvidenceDetail.Poll);
            if (detail) return;
            T("Dialogue", Dialogue.Poll);
            T("Investigation", Investigation.Poll);
            T("Logic", LogicScreen.Poll);
            bool gk2 = Gk2Map.InGame;
            if (!gk2)
            {
                // First game only.
                T("ExamineScreen", ExamineScreen.Poll);
                T("Examine3D", Examine3D.Poll);
                T("Gauge", Gauge.Poll);
#if DEVTOOLS
                T("PageExport", PageExport.Poll);
#endif
                T("Rooms", Rooms.Poll);
                T("Navigator", Navigator.Poll);
            }
            else
            {
                T("Gk2Text", Gk2Text.Poll);
                T("LogicChess", LogicChess.Poll);
                T("Gk2Gauge", Gk2Gauge.Poll);
                T("Gk2Nav", Gk2Nav.Poll);
                T("Gk2Examine", Gk2Examine.Poll);
                T("Gk2Examine3D", Gk2Examine3D.Poll);
                T("Gk2Item2D", Gk2Item2D.Poll);
                T("Gk2Video", Gk2Video.Poll);
                T("Gk2Luminol", Gk2Luminol.Poll);
                T("Gk2Finger", Gk2Finger.Poll);
            }
        }

        private static readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        private static float _slowLogAt;

        /// <summary>Runs one part of the mod's per-frame work and notes in the log when it took long enough to be felt.</summary>
        private static void T(string name, Action a)
        {
            _sw.Restart();
            a();
            long ms = _sw.ElapsedMilliseconds;
            if (ms >= 15 && UnityEngine.Time.unscaledTime - _slowLogAt > 0.5f) { _slowLogAt = UnityEngine.Time.unscaledTime; Log.Msg($"Slow frame: {name} took {ms} ms"); }
        }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        public static bool GameHasFocus => GameFocused();

        private static bool GameFocused()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        /// <summary>True once when a key goes down while the game window has focus.</summary>
        private bool Pressed(int vk)
        {
            bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
            bool was = _keyDown[vk];
            _keyDown[vk] = down;
            return down && !was && GameFocused();
        }
    }

    internal static class Dialogue
    {
        private static string _lastLine = "";
        private static string _pendingLine = "";
        private static float _pendingSince, _pendingLastSeen, _emptySince = -1f;
        private static bool _pendingSkipped, _vsSeen;
        private static float _vsLastAt = -10f;
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        /// <summary>The player pressed a key that advances or skips text (Space, Enter, left mouse button).</summary>
        private static bool AdvanceKeyDown() =>
            (GetAsyncKeyState(0x20) & 0x8000) != 0 || (GetAsyncKeyState(0x0D) & 0x8000) != 0 || (GetAsyncKeyState(0x01) & 0x8000) != 0;
        /// <summary>Read a line we never got to only if it was on screen just now and the player didn't skip it.</summary>
        private static bool CatchUpOk(float now) => !_pendingSkipped && now - _pendingLastSeen < 0.5f;
        private static float _nextScan;
        private static string _lastErr;
        internal static float LastSpokenAt = -10f;
        /// <summary>When a line of dialogue was last on screen (for pausing sounds during conversations).</summary>
        internal static float MessageSeenAt = -10f;
        /// <summary>The speaker of the last line read, and when (used to learn people's names).</summary>
        internal static string LastSpeaker = "";
        internal static float LastSpeakerAt = -10f;
        private static MessageWindowControl[] _controls = new MessageWindowControl[0];

        /// <summary>Builds "Name: text" for a window, or "" when it has no text.</summary>
        private static string LineOf(MessageWindow win)
        {
            string text = TextCleaner.Clean(win.Message?.GetMessageText());
            if (string.IsNullOrEmpty(text)) return "";
            string name = "";
            try { if (win.CharacterNameRoot != null && win.CharacterNameRoot.activeInHierarchy) name = TextCleaner.Clean(win.CharacterName); } catch { }
            if (!string.IsNullOrEmpty(name) && name != LastSpeaker) { LastSpeaker = name; Navigator.HeardSpeaker(name); Gk2Nav.HeardSpeaker(name); }
            if (!string.IsNullOrEmpty(name)) LastSpeakerAt = UnityEngine.Time.unscaledTime;
            return string.IsNullOrEmpty(name) ? text : name + ": " + text;
        }

        private static MessageWindow FindActiveWindow(out bool waiting) => FindActiveWindow(out waiting, out _);

        private static MessageWindow FindActiveWindow(out bool waiting, out bool vsArrows)
        {
            waiting = false; vsArrows = false;
            float now = UnityEngine.Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + 1f;
                var found = UnityEngine.Object.FindObjectsOfType<MessageWindowControl>();
                _controls = new MessageWindowControl[found.Length];
                for (int i = 0; i < found.Length; i++) _controls[i] = found[i];
            }
            foreach (var ctrl in _controls)
            {
                if (ctrl == null || ctrl.WasCollected || !ctrl.isActiveAndEnabled) continue;
                var win = ctrl.ActiveWindow;
                if (win == null || !win.gameObject.activeInHierarchy) continue;
                try { waiting = ctrl.m_ArrowActiveWait || (win.m_WaitArrow != null && win.m_WaitArrow.activeInHierarchy); } catch { }
                // Testimony shows "previous / next statement" arrows instead of the usual "continue" arrow.
                try
                {
                    vsArrows = ctrl.m_VSArrowActiveLeft || ctrl.m_VSArrowActiveRight
                        || (win.m_VSArrowLeft != null && win.m_VSArrowLeft.activeInHierarchy)
                        || (win.m_VSArrowRight != null && win.m_VSArrowRight.activeInHierarchy);
                }
                catch { }
                return win;
            }
            return null;
        }

        /// <summary>Called every frame: speaks a line once it has finished appearing.</summary>
        public static void Poll()
        {
            try
            {
                var win = FindActiveWindow(out bool waiting, out bool vsArrows);
                float tnow = UnityEngine.Time.unscaledTime;
                string line = win == null ? "" : LineOf(win);
                // Once the message window has been closed or empty for a moment, the same line may be shown
                // again (e.g. examining the same thing twice) and must be read again.
                if (string.IsNullOrEmpty(line))
                {
                    if (_emptySince < 0) _emptySince = tnow;
                    else if (tnow - _emptySince > 0.5f && _lastLine.Length > 0 && (_pendingLine == _lastLine || string.IsNullOrEmpty(_pendingLine))) { _lastLine = ""; _pendingLine = ""; }
                }
                else { _emptySince = -1f; MessageSeenAt = tnow; }
                if (win == null) return;
                if (!string.IsNullOrEmpty(line) && line == _pendingLine) _pendingLastSeen = tnow;
                if (!string.IsNullOrEmpty(_pendingLine) && _pendingLine != _lastLine && AdvanceKeyDown()) _pendingSkipped = true;
                if (string.IsNullOrEmpty(line) || line == _lastLine)
                {
                    // Window cleared before we read the line (it auto-advanced): read what we had.
                    if (string.IsNullOrEmpty(line) && !string.IsNullOrEmpty(_pendingLine) && _pendingLine != _lastLine && _pendingLine.Length > 3
                        && UnityEngine.Time.unscaledTime - _pendingSince > 0.3f && CatchUpOk(tnow))
                    {
                        _lastLine = _pendingLine;
                        Speech.Say(_pendingLine, interrupt: false);
                    }
                    _pendingLine = line; return;
                }

                float now = UnityEngine.Time.unscaledTime;
                if (line != _pendingLine)
                {
                    // A different line replaced one we never read (it advanced on its own): read the old one now.
                    if (!string.IsNullOrEmpty(_pendingLine) && _pendingLine != _lastLine && !line.StartsWith(_pendingLine) && _pendingLine.Length > 3
                        && now - _pendingSince > 0.3f && CatchUpOk(now))   // only lines that had finished typing, and weren't skipped
                    {
                        Main.Log.Msg("Catching up skipped line");
                        _lastLine = _pendingLine;
                        Speech.Say(_pendingLine, interrupt: false);
                    }
                    _pendingLine = line; _pendingSince = now; _pendingLastSeen = now; _pendingSkipped = false;
                }

                // Speak when the "continue" arrow shows, or when the text has stopped changing for a while
                // (some lines advance on their own without an arrow).
                // Testimony: the statement arrows show; speak once the text has stopped changing for a moment.
                if (vsArrows != _vsSeen) { _vsSeen = vsArrows; Main.Log.Msg("Testimony arrows " + (vsArrows ? "shown" : "hidden")); }
                if (vsArrows) _vsLastAt = now;
                bool testimony = now - _vsLastAt < 4f;
                if (vsArrows && now - _pendingSince > 0.15f) waiting = true;
                // Enter / Space shows the whole statement at once: read it as soon as it stops changing.
                if (testimony && _pendingSkipped && now - _pendingSince > 0.12f) waiting = true;
                // Wait for the typing to settle: the second game shows the arrow during dramatic pauses and while
                // shouting a line one letter at a time. A line stopped in the middle of a word waits longer.
                char lastCh = line[line.Length - 1];
                float settle = char.IsLetterOrDigit(lastCh) ? 1.0f : 0.15f;
                if ((waiting && now - _pendingSince > settle) || now - _pendingSince > 1.5f)
                {
                    // The line was read early and has since grown (the game was still typing it):
                    // read only the new part, so nothing is repeated.
                    bool more = _lastLine.Length > 0 && line.Length > _lastLine.Length && line.StartsWith(_lastLine) && now - LastSpokenAt < 10f;
                    string say = more ? line.Substring(_lastLine.Length).Trim() : line;
                    _lastLine = line;
                    LastSpokenAt = UnityEngine.Time.unscaledTime;
                    if (more) { Main.Log.Msg("Rest of line: " + say); Speech.Say(say, false, false); Speech.Remember(line); }
                    else Speech.Say(line);
                }
            }
            catch (Exception e) { if (e.Message != _lastErr) { _lastErr = e.Message; Main.Log.Error("Dialogue poll failed: " + e); } _nextScan = 0; }
        }

        /// <summary>
        /// Choices opened: read the question in the message window first if it hasn't been read yet
        /// (the game shows the choices before the question has finished "typing"). True if it spoke.
        /// </summary>
        public static bool FlushPending()
        {
            try
            {
                var win = FindActiveWindow(out _);
                string line = win == null ? "" : LineOf(win);
                if (string.IsNullOrEmpty(line) || line == _lastLine) return false;
                bool more = _lastLine.Length > 0 && line.StartsWith(_lastLine);
                string say = more ? line.Substring(_lastLine.Length).Trim() : line;
                _lastLine = line; _pendingLine = line;
                LastSpokenAt = UnityEngine.Time.unscaledTime;
                Main.Log.Msg("Question before choices: " + say);
                Speech.Say(say, !more, false); Speech.Remember(line);
                return true;
            }
            catch { return false; }
        }

        /// <summary>F3: read the message window right now.</summary>
        public static void ReadCurrent()
        {
            try
            {
                _nextScan = 0;
                var win = FindActiveWindow(out _);
                string line = win == null ? "" : LineOf(win);
                if (string.IsNullOrEmpty(line)) { Speech.Say("No message on screen.", true, false); return; }
                _lastLine = line;
                Speech.Say(line);
            }
            catch (Exception e) { Main.Log.Error("ReadCurrent failed: " + e); }
        }
    }
}
