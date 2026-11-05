using System;
using System.Runtime.InteropServices;

namespace AAIAccess
{
    /// <summary>
    /// Sends text to NVDA through its controller client, or to Windows SAPI when NVDA is not running.
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
        public static void Remember(string text) { if (!string.IsNullOrWhiteSpace(text)) LastSpoken = text; }

        private static bool NvdaAvailable()
        {
            if (_nvdaDllMissing) return false;
            try { return nvdaController_testIfRunning() == 0; }
            catch (DllNotFoundException) { _nvdaDllMissing = true; Main.Log.Warning("nvdaControllerClient64.dll not found; using Windows speech."); return false; }
            catch (Exception e) { Main.Log.Warning("NVDA check failed: " + e.Message); return false; }
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
            if (interrupt && Now() < _protectUntil) interrupt = false;   // let the announcement finish
            if (remember) LastSpoken = text;
            Main.Log.Msg("Speak: " + text);
            try
            {
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

        public static void Stop()
        {
            try
            {
                if (NvdaAvailable()) nvdaController_cancelSpeech();
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
