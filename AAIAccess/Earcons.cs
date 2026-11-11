using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AAIAccess
{
    /// <summary>
    /// Short sounds played through Windows directly. Unlike speech they are not cut off
    /// by NVDA when a key is pressed, so they work while the player is walking.
    /// </summary>
    internal static class Earcons
    {
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(byte[] sound, IntPtr hmod, uint flags);
        private const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;

        // Kept alive in static fields: PlaySound(SND_ASYNC) reads the buffer while playing.
        public static readonly byte[] Examine = Tones((880, 70), (0, 30), (1175, 90));          // high double: something to examine
        public static readonly byte[] ExamineChecked = Tones((880, 90));                       // high single: already examined
        public static readonly byte[] Talk = Tones((392, 80), (0, 30), (523, 100));            // low double: someone to talk to
        public static readonly byte[] TalkChecked = Tones((392, 100));                         // low single: already talked
        public static readonly byte[] Spot = Tones((1047, 50));                                // examination cursor entered a spot
        public static readonly byte[] SpotChecked = Tones((698, 50));                          // ... a spot already checked

        /// <summary>When an Examine / Talk / spot sound last played (the tracking sound waits for it).</summary>
        public static float LastPromptSoundAt = -10f;

        public static void Play(byte[] wav)
        {
            bool positional = wav == _lastPositional;
            if (!positional) { try { LastPromptSoundAt = UnityEngine.Time.unscaledTime; } catch { } }
            // Through the game's own audio first: Windows' PlaySound is silent on some PCs.
            if (PlayUnity(wav, positional)) return;
            try
            {
                if (!positional) Pin(wav);
                if (!PlaySound(wav, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT) && _failLogged++ < 3)
                    Main.Log.Warning("Earcon: Windows couldn't play the sound (error " + Marshal.GetLastWin32Error() + ")");
            }
            catch (Exception e) { Main.Log.Warning("Earcon failed: " + e.Message); }
        }

        private static int _failLogged;
        private static readonly System.Collections.Generic.HashSet<byte[]> _pinned = new System.Collections.Generic.HashSet<byte[]>();

        /// <summary>PlaySound keeps reading the buffer after it returns: keep it where it is.</summary>
        private static void Pin(byte[] wav) { if (_pinned.Add(wav)) GCHandle.Alloc(wav, GCHandleType.Pinned); }

        // ---------- the game's own audio (Unity) ----------
        private static UnityEngine.GameObject _audioObj;
        private static UnityEngine.AudioSource _srcPrompt, _srcTrack;
        private static readonly System.Collections.Generic.Dictionary<byte[], UnityEngine.AudioClip> _clips = new System.Collections.Generic.Dictionary<byte[], UnityEngine.AudioClip>();
        private static UnityEngine.AudioClip _trackClip;
        private static bool _unityBroken, _unityLogged;

        private static bool PlayUnity(byte[] wav, bool positional)
        {
            if (_unityBroken) return false;
            try
            {
                if (_audioObj == null || _audioObj.WasCollected || _srcPrompt == null || _srcPrompt.WasCollected)
                {
                    _audioObj = new UnityEngine.GameObject("AAIAccess_Sounds");
                    UnityEngine.Object.DontDestroyOnLoad(_audioObj);
                    _srcPrompt = _audioObj.AddComponent<UnityEngine.AudioSource>();
                    _srcTrack = _audioObj.AddComponent<UnityEngine.AudioSource>();
                    foreach (var src in new[] { _srcPrompt, _srcTrack }) { src.playOnAwake = false; src.loop = false; src.volume = 1f; src.priority = 0; }
                }
                UnityEngine.AudioClip clip;
                if (positional)
                {
                    if (_trackClip != null && !_trackClip.WasCollected) { _srcTrack.Stop(); UnityEngine.Object.Destroy(_trackClip); }
                    clip = _trackClip = MakeClip(wav);
                }
                else if (!_clips.TryGetValue(wav, out clip) || clip == null || clip.WasCollected) _clips[wav] = clip = MakeClip(wav);
                var s = positional ? _srcTrack : _srcPrompt;
                s.clip = clip; s.Play();
                if (!_unityLogged) { _unityLogged = true; Main.Log.Msg("Earcons: playing through the game's audio"); }
                return true;
            }
            catch (Exception e)
            {
                _unityBroken = true;
                Main.Log.Warning("Earcons: the game's audio didn't work, using Windows sounds instead: " + e.Message);
                return false;
            }
        }

        /// <summary>A Unity clip from one of our 16-bit PCM WAVs (mono or stereo).</summary>
        private static UnityEngine.AudioClip MakeClip(byte[] wav)
        {
            int channels = BitConverter.ToInt16(wav, 22), rate = BitConverter.ToInt32(wav, 24);
            int dataLen = BitConverter.ToInt32(wav, 40);
            int n = dataLen / 2;
            var data = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>(n);
            for (int i = 0; i < n; i++) data[i] = BitConverter.ToInt16(wav, 44 + i * 2) / 32768f;
            var clip = UnityEngine.AudioClip.Create("AAIAccess", n / channels, channels, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static byte[] _lastPositional;   // kept alive while it plays
        private static GCHandle _pin;

        /// <summary>
        /// A short stereo tone placed in space: pan -1 (left) .. 1 (right), pitch in Hz, volume 0..1.
        /// notes &gt; 1 repeats it (used for "you're there").
        /// </summary>
        public static void PlayPositional(double pan, double freq, double volume, int notes = 1)
        {
            const int rate = 22050;
            int toneN = rate * 120 / 1000, gapN = rate * 50 / 1000;
            int total = notes * toneN + (notes - 1) * gapN;
            pan = Math.Max(-1, Math.Min(1, pan));
            double angle = (pan + 1) * Math.PI / 4;               // equal-power panning
            double gl = Math.Cos(angle), gr = Math.Sin(angle);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + total * 4); w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)2);
            w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(total * 4);
            for (int k = 0; k < notes; k++)
            {
                double f = freq * (k == 0 ? 1 : 1.5);
                for (int i = 0; i < toneN; i++)
                {
                    double env = Math.Min(1.0, Math.Min(i, toneN - i) / (rate * 0.008));
                    // A little harmonic makes direction easier to hear than a pure sine.
                    double v = (Math.Sin(2 * Math.PI * f * i / rate) + 0.3 * Math.Sin(4 * Math.PI * f * i / rate)) * env * 0.4 * volume;
                    w.Write((short)(v * gl * short.MaxValue)); w.Write((short)(v * gr * short.MaxValue));
                }
                if (k < notes - 1) for (int i = 0; i < gapN; i++) { w.Write((short)0); w.Write((short)0); }
            }
            w.Flush();
            // Pinned so the garbage collector can't move it while Windows is still playing it.
            if (_pin.IsAllocated) { PlaySound(null, IntPtr.Zero, 0); _pin.Free(); }
            _lastPositional = ms.ToArray();
            _pin = GCHandle.Alloc(_lastPositional, GCHandleType.Pinned);
            Play(_lastPositional);
        }

        /// <summary>Builds a WAV from (frequency Hz, milliseconds) pairs; frequency 0 is silence.</summary>
        private static byte[] Tones(params (int freq, int ms)[] parts)
        {
            const int rate = 22050;
            int total = 0;
            foreach (var p in parts) total += rate * p.ms / 1000;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + total * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(total * 2);
            foreach (var p in parts)
            {
                int n = rate * p.ms / 1000;
                for (int i = 0; i < n; i++)
                {
                    double env = Math.Min(1.0, Math.Min(i, n - i) / (rate * 0.008));   // 8 ms fade in/out, no clicks
                    double v = p.freq == 0 ? 0 : Math.Sin(2 * Math.PI * p.freq * i / rate) * env * 0.35;
                    w.Write((short)(v * short.MaxValue));
                }
            }
            w.Flush();
            return ms.ToArray();
        }
    }
}
