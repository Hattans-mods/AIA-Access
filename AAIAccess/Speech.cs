using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AAIAccess
{
    /// <summary>
    /// Speaks through the Prism library (prism.dll next to GK12.exe), which picks the running screen reader
    /// (NVDA, JAWS, ZDSR, ...) or a Windows voice. Without prism.dll, or with "noprism" in
    /// UserData/AAIAccess_off.txt, it uses the old path: NVDA's controller client, then Windows SAPI.
    /// </summary>
    internal static class Speech
    {
        [DllImport("nvdaControllerClient64.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_testIfRunning();

        [DllImport("nvdaControllerClient64.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_speakText(string text);

        [DllImport("nvdaControllerClient64.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_cancelSpeech();

        [DllImport("nvdaControllerClient64.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_brailleMessage(string text);

        private static bool _nvdaDllMissing;
        private static object _sapiVoice;

        public static string LastSpoken { get; private set; } = "";
        /// <summary>Set what F2 repeats without speaking it.</summary>
        public static void Remember(string text) { if (!string.IsNullOrWhiteSpace(text)) LastSpoken = Loc.Translate(text); }

        private static bool NvdaAvailable()
        {
            if (_nvdaDllMissing) return false;
            try { return nvdaController_testIfRunning() == 0; }
            catch (DllNotFoundException) { _nvdaDllMissing = true; Main.Log.Warning("nvdaControllerClient64.dll not found; using Windows speech."); return false; }
            catch (Exception e) { Main.Log.Warning("NVDA check failed: " + e.Message); return false; }
        }

        // ---- Prism (tested with prism.dll 0.18.2) ----
        private const string PrismDll = "prism.dll";
        [DllImport(PrismDll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr prism_version_string();
        // PrismConfig is returned by value; on x64 a struct this size comes back through a hidden pointer
        // to memory the caller provides, which is what this declaration passes.
        [DllImport(PrismDll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr prism_config_init(IntPtr result);
        [DllImport(PrismDll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr prism_init(IntPtr config);
        [DllImport(PrismDll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr prism_registry_create_best(IntPtr ctx);
        [DllImport(PrismDll, CallingConvention = CallingConvention.Cdecl)] private static extern int prism_backend_initialize(IntPtr backend);
        [DllImport(PrismDll, CallingConvention = CallingConvention.Cdecl)] private static extern void prism_backend_free(IntPtr backend);
        [DllImport(PrismDll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr prism_backend_name(IntPtr backend);
        [DllImport(PrismDll, CallingConvention = CallingConvention.Cdecl)] private static extern int prism_backend_output(IntPtr backend, byte[] utf8, [MarshalAs(UnmanagedType.U1)] bool interrupt);
        [DllImport(PrismDll, CallingConvention = CallingConvention.Cdecl)] private static extern int prism_backend_stop(IntPtr backend);

        private const int PrismOk = 0, PrismAlreadyInitialized = 15;
        private static bool _prismTried, _prismOff;
        private static IntPtr _prismCtx, _prismBackend;
        private static string _prismName = "";
        private static float _prismNextCheck;

        /// <summary>A plain Windows voice, not a screen reader: worth checking again later in case one starts.</summary>
        private static bool PrismIsPlainVoice => _prismName == "SAPI" || _prismName == "OneCore" || _prismName == "UIA";

        private static bool PrismReady()
        {
            if (_prismOff) return false;
            if (!_prismTried)
            {
                _prismTried = true;
                try
                {
                    string f = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_off.txt");
                    if (System.IO.File.Exists(f) && System.IO.File.ReadAllText(f).ToLowerInvariant().Contains("noprism"))
                    { _prismOff = true; Main.Log.Msg("Prism switched off (AAIAccess_off.txt says noprism)."); return false; }
                    IntPtr cfg = Marshal.AllocHGlobal(256);   // kept for the life of the game
                    for (int i = 0; i < 256; i++) Marshal.WriteByte(cfg, i, 0);
                    prism_config_init(cfg);
                    _prismCtx = prism_init(cfg);
                    if (_prismCtx == IntPtr.Zero) { _prismOff = true; Main.Log.Warning("Prism could not start; using NVDA or Windows speech."); return false; }
                    Main.Log.Msg("Prism " + Marshal.PtrToStringUTF8(prism_version_string()) + " loaded.");
                    PrismPickBest();
                }
                catch (DllNotFoundException) { _prismOff = true; Main.Log.Warning("prism.dll not found; using NVDA or Windows speech."); return false; }
                catch (Exception e) { _prismOff = true; Main.Log.Warning("Prism failed to start: " + e.Message); return false; }
            }
            // A screen reader started after the game: switch to it.
            if (PrismIsPlainVoice && Now() >= _prismNextCheck) PrismPickBest();
            return _prismBackend != IntPtr.Zero;
        }

        /// <summary>Ask Prism for the best output now, and keep it if it differs from the current one.</summary>
        private static void PrismPickBest()
        {
            _prismNextCheck = Now() + 5f;
            try
            {
                IntPtr b = prism_registry_create_best(_prismCtx);
                if (b == IntPtr.Zero) return;
                int r = prism_backend_initialize(b);
                string name = Marshal.PtrToStringUTF8(prism_backend_name(b)) ?? "";
                if ((r != PrismOk && r != PrismAlreadyInitialized) || (name == _prismName && _prismBackend != IntPtr.Zero))
                { prism_backend_free(b); return; }
                if (_prismBackend != IntPtr.Zero) prism_backend_free(_prismBackend);
                _prismBackend = b;
                _prismName = name;
                Main.Log.Msg("Prism speaks through " + name + ".");
            }
            catch (Exception e) { Main.Log.Warning("Prism backend check failed: " + e.Message); }
        }

        private static bool PrismSay(string text, bool interrupt)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text.Replace('\0', ' ') + "\0");
            int r = prism_backend_output(_prismBackend, utf8, interrupt);
            if (r == PrismOk) return true;
            // The screen reader may have closed: pick again and try once more.
            Main.Log.Warning("Prism (" + _prismName + ") could not speak, error " + r + ".");
            string before = _prismName;
            PrismPickBest();
            if (_prismName == before) return false;
            return prism_backend_output(_prismBackend, utf8, interrupt) == PrismOk;
        }

        // A screen's opening announcement ("Logic. 2 pieces. Press F1 for help.") must not be cut off by
        // whatever the screen reads right after it: until it has had time to be spoken, later speech queues.
        private static float _protectUntil;

        /// <summary>Speak an announcement that later speech must not cut off.</summary>
        public static void Announce(string text, bool interrupt = false)
        {
            Say(text, interrupt, false);
            float now = Now();
            // Rough speaking time: about 14 characters a second, at most 4 seconds.
            _protectUntil = now + Math.Min(4f, 0.4f + text.Length / 14f);
        }

        private static float Now() { try { return UnityEngine.Time.unscaledTime; } catch { return 0; } }

        /// <summary>Speak text. interrupt=true stops any speech already in progress.</summary>
        public static void Say(string text, bool interrupt = true, bool remember = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = Loc.Translate(text);   // the mod's own words in the game's language
            if (interrupt && Now() < _protectUntil) interrupt = false;   // let the announcement finish
            if (remember) LastSpoken = text;
            Main.Log.Msg("Speak: " + text);
            try
            {
                if (PrismReady() && PrismSay(text, interrupt)) return;   // speech and braille
                if (NvdaAvailable())
                {
                    if (interrupt) nvdaController_cancelSpeech();
                    nvdaController_speakText(text);
                    nvdaController_brailleMessage(text);
                    return;
                }
                SapiSay(text, interrupt);
            }
            catch (Exception e) { Main.Log.Error("Speech failed: " + e); }
        }

        /// <summary>The player pressed something: stop talking, as a screen reader does on a key press (it never sees
        /// controller buttons), and let what comes next interrupt freely.</summary>
        public static void UserInterrupt()
        {
            _protectUntil = 0;
            Stop();
        }

        public static void Stop()
        {
            try
            {
                if (PrismReady()) prism_backend_stop(_prismBackend);
                else if (NvdaAvailable()) nvdaController_cancelSpeech();
                else if (_sapiVoice != null) SapiSay("", true);
            }
            catch { }
        }

        private static void SapiSay(string text, bool interrupt)
        {
            if (_sapiVoice == null)
            {
                var t = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (t == null) { Main.Log.Warning("Windows speech (SAPI) is not available."); return; }
                _sapiVoice = Activator.CreateInstance(t);
            }
            // 1 = SVSFlagsAsync, 2 = SVSFPurgeBeforeSpeak
            int flags = 1 | (interrupt ? 2 : 0);
            _sapiVoice.GetType().InvokeMember("Speak", System.Reflection.BindingFlags.InvokeMethod, null, _sapiVoice, new object[] { text, flags });
        }
    }
}
