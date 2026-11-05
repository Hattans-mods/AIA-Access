extern alias GK2;
using System;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: the text "types" one letter every few frames, and a line is only read once it has finished.
    /// Speed the typing up to one letter per frame so lines are read sooner (dramatic pauses are separate commands and stay).</summary>
    internal static class Gk2Text
    {
        private static float _next;
        private static bool _logged;

        public static void Poll()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.05f;
            try
            {
                var sc = G2.cScenario.Instance; if (sc == null) return;
                var arr = sc.mScript; if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                {
                    var a = arr[i]?.analyzer; if (a == null) continue;
                    if (a.mSpeed > 1)
                    {
                        if (!_logged) { _logged = true; Main.Log.Msg($"GK2 text speed was {a.mSpeed} (default {a.mSpeedDefault}); setting 1"); }
                        a.mSpeed = 1;
                    }
                }
            }
            catch { }
        }
    }
}
