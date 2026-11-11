using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using Il2CppGK_Common.UI.Message;

// The version lives in one place (Main.Version); the installer and the update check read the assembly version.
[assembly: MelonInfo(typeof(AAIAccess.Main), "AAI Access", AAIAccess.Main.Version, "Hattans-mods")]
[assembly: MelonGame("CAPCOM Co., Ltd", "GK12")]
[assembly: System.Reflection.AssemblyVersion(AAIAccess.Main.Version)]
[assembly: System.Reflection.AssemblyFileVersion(AAIAccess.Main.Version)]

namespace AAIAccess
{
    public class Main : MelonMod
    {
        /// <summary>The mod's version. Raise it for every release; GitHub release tags are "V" + this.</summary>
        public const string Version = "1.1.0";

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
            Gk2Ahead.Apply(HarmonyInstance);
            Pad.SelectScreen = SelectScreen;
            Pad.Init(HarmonyInstance);
            UpdateCheck.Start();
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
            try { UpdateCheck.Poll(); } catch { }
#if DEVTOOLS
            KeyMapDump.Poll();   // once, ~10 s after start: the game's button assignments to the log
#endif
            Pad.Poll();   // controller: hold the mod button (left stick button) and press another; see Pad.cs
            if (Pressed(0x70)) // F1: help for the current screen, with the keyboard's mod keys
                Help.Speak(false);
            else if (PadChord(Pad.B.Start)) // controller help chord: the same, with the controller's mod keys instead
                Help.Speak(true);
            if (Pressed(0x71) || PadChord(Pad.B.South)) // F2: repeat last spoken line
                Speech.Say(string.IsNullOrEmpty(Speech.LastSpoken) ? "Nothing to repeat yet." : Speech.LastSpoken, true, false);
            if (Pressed(0x72) || PadChord(Pad.B.East)) // F3: status of the current puzzle
            {
                if (Examine3D.Active) Examine3D.ReadStatus();          // 3D first: it opens from the Organizer's evidence pages
                else if (Gk2Examine3D.Active) Gk2Examine3D.ReadStatus();
                else if (EvidenceDetail.Active) EvidenceDetail.ReadStatus();
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
            if (Pressed(0x48) || PadChord(Pad.B.West)) { if (!LogicChess.SpeakTime() && !Gk2Gauge.Speak()) Gauge.Speak(); }   // H: truth gauge
            if (Pressed(0x73))   // F4: room name; Shift+F4: rename the room from the clipboard
            {
                if ((GetAsyncKeyState(0x10) & 0x8000) != 0) Rooms.RenameFromClipboard(); else Rooms.Speak();
            }
            if (PadChord(Pad.B.North)) Rooms.Speak();
            if (Pressed(0xDB) | PadChord(Pad.B.L2) | PadChord(Pad.B.Left) | PadSolo(Pad.B.L1)) { if (Gk2Video.Active) Gk2Video.Step(-1); else if (Gk2Luminol.Active) Gk2Luminol.Step(-1); else if (Gk2Finger.Active) Gk2Finger.Step(-1); else if (ExamineScreen.Active) ExamineScreen.Step(-1); else if (Gk2Examine.Active) Gk2Examine.Step(-1); else if (Gk2Examine3D.Active) Gk2Examine3D.Step(-1); else if (Examine3D.Active) Examine3D.Step(-1); else if (Gk2Nav.Active) Gk2Nav.Step(-1); else Navigator.Step(-1); }   // [ previous
            if (Pressed(0xDD) | PadChord(Pad.B.R2) | PadChord(Pad.B.Right) | PadSolo(Pad.B.R1)) { if (Gk2Video.Active) Gk2Video.Step(+1); else if (Gk2Luminol.Active) Gk2Luminol.Step(+1); else if (Gk2Finger.Active) Gk2Finger.Step(+1); else if (ExamineScreen.Active) ExamineScreen.Step(+1); else if (Gk2Examine.Active) Gk2Examine.Step(+1); else if (Gk2Examine3D.Active) Gk2Examine3D.Step(+1); else if (Examine3D.Active) Examine3D.Step(+1); else if (Gk2Nav.Active) Gk2Nav.Step(+1); else Navigator.Step(+1); }   // ] next
            if ((Pressed(0xDC) | PadChord(Pad.B.R1) | PadChord(Pad.B.Down)) && !ExamineScreen.Active && !Gk2Examine.Active && !Gk2Examine3D.Active && !Gk2Video.Active && !Gk2Luminol.Active && !Gk2Finger.Active) { if (Gk2Nav.Active) Gk2Nav.Walk(); else Navigator.WalkToSelected(); }
            if ((Pressed(0xDE) | PadChord(Pad.B.L1) | PadChord(Pad.B.Up)) && !ExamineScreen.Active && !Gk2Examine.Active && !Gk2Examine3D.Active && !Gk2Video.Active && !Gk2Luminol.Active && !Gk2Finger.Active) { if (Gk2Nav.Active) Gk2Nav.Ping(); else Navigator.Ping(); }   // ' sound from where it is   // \ walk to it
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

        /// <summary>True once when this controller button is pressed while the mod button is held.</summary>
        private static bool PadChord(Pad.B b) => Pad.Chord(b) && GameFocused();
        private static bool PadSolo(Pad.B b) => Pad.Solo(b) && GameFocused();

        /// <summary>Screens where L1 / R1 alone pick the previous / next thing (what [ and ] step through):
        /// walking in an investigation, and the examine, 3D, video, luminol and fingerprint screens.</summary>
        private static bool SelectScreen()
        {
            // 3D examination first: it opens from the Organizer (an evidence's Check), with the Organizer still open.
            if (Examine3D.Active || Gk2Examine3D.Active) return true;
            if (Organizer.Active || LogicScreen.Active || EvidenceDetail.Active) return false;
            if (ExamineScreen.Active || Examine3D.Active || Gk2Examine.Active || Gk2Examine3D.Active
                || Gk2Video.Active || Gk2Luminol.Active || Gk2Finger.Active) return true;
            return Gk2Map.InGame ? Gk2Nav.Active : Investigation.PlayerCanWalk;
        }
    }

    internal static class Dialogue
    {
        private static string _lastLine = "";
        private static string _pendingLine = "";
        /// <summary>A whole page read out before the game finished typing it (first game): while the window
        /// shows the start of it, nothing more is read.</summary>
        private static string _early = "", _earlyTyped = "", _aheadTried = "";
        private static float _pendingSince, _pendingLastSeen, _emptySince = -1f;
        private static bool _pendingSkipped, _vsSeen;
        private static float _vsLastAt = -10f;
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        /// <summary>The player pressed a key that advances or skips text (Space, Enter, left mouse button, controller confirm).</summary>
        private static bool AdvanceKeyDown() =>
            KeyMap.Down(Il2CppGK_System.Controller.ButtonType.A) || (GetAsyncKeyState(0x0D) & 0x8000) != 0 || (GetAsyncKeyState(0x01) & 0x8000) != 0 || Pad.ConfirmDown;
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
        private static string LineOf(MessageWindow win) => LineOf(win, out _, out _);

        private static string LineOf(MessageWindow win, out string text, out string speaker)
        {
            speaker = "";
            text = TextCleaner.Clean(win.Message?.GetMessageText());
            if (string.IsNullOrEmpty(text)) return "";
            string name = "";
            try { if (win.CharacterNameRoot != null && win.CharacterNameRoot.activeInHierarchy) name = TextCleaner.Clean(win.CharacterName); } catch { }
            if (!string.IsNullOrEmpty(name) && name != LastSpeaker) { LastSpeaker = name; Navigator.HeardSpeaker(name); Gk2Nav.HeardSpeaker(name); }
            if (!string.IsNullOrEmpty(name)) LastSpeakerAt = UnityEngine.Time.unscaledTime;
            speaker = name;
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
                string text = "", speaker = "";
                string line = win == null ? "" : LineOf(win, out text, out speaker);
                // Once the message window has been closed or empty for a moment, the same line may be shown
                // again (e.g. examining the same thing twice) and must be read again.
                if (string.IsNullOrEmpty(line))
                {
                    if (_emptySince < 0) _emptySince = tnow;
                    else if (tnow - _emptySince > 0.5f && _lastLine.Length > 0 && (_pendingLine == _lastLine || string.IsNullOrEmpty(_pendingLine))) { _lastLine = ""; _pendingLine = ""; _early = ""; }
                }
                else { _emptySince = -1f; MessageSeenAt = tnow; }
                // A new page starts with an empty window, or with text that doesn't continue the last.
                if (_early.Length > 0 && (string.IsNullOrEmpty(text) || !Gk1Ahead.Norm(text).StartsWith(_earlyTyped, StringComparison.Ordinal))) _early = "";
                if (_early.Length > 0) _earlyTyped = Gk1Ahead.Norm(text);
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
                // Read the whole page as soon as it starts, from the game's script (both games).
                if (_early.Length > 0)
                {
                    if (Gk1Ahead.Norm(_early).StartsWith(Gk1Ahead.Norm(text), StringComparison.Ordinal)) { _lastLine = line; _pendingLine = line; _pendingSince = now; _pendingLastSeen = now; return; }
                    _early = "";
                }
                if (text != _aheadTried)
                {
                    _aheadTried = text;
                    string page = Gk2Map.InGame ? Gk2Ahead.Page(text) : Gk1Ahead.Page(text);
                    if (!string.IsNullOrEmpty(page))
                    {
                        string full = string.IsNullOrEmpty(speaker) ? page : speaker + ": " + page;
                        Main.Log.Msg($"Whole page read early ({text.Length} of {page.Length} letters typed)");
                        Speech.Say(full);
                        _early = page; _earlyTyped = Gk1Ahead.Norm(text); _lastLine = line; _pendingLine = line; _pendingSince = now; _pendingLastSeen = now;
                        LastSpokenAt = now;
                        return;
                    }
                }
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
                // Chinese, Japanese and Korean have no spaces between words, so a line can end on any character:
                // only the Latin alphabet (and digits) can be "in the middle of a word".
                bool midWord = char.IsLetterOrDigit(lastCh) && lastCh < 0x2E80;
                float settle = midWord ? 1.0f : 0.15f;
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
