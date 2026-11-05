extern alias GK1;
using System;

namespace AAIAccess
{
    /// <summary>
    /// The truth gauge (first game): goes down with every wrong answer; at zero the game is over.
    /// The game keeps it as a number from 0 to 50 (GKJ1.MAX_HP); the mod says it as a percentage.
    /// H reads it; changes are announced.
    /// </summary>
    internal static class Gauge
    {
        private const int Max = 50;
        private static int _last = -1, _from = -1;
        private static float _settleAt = -1f;

        private static int Now()
        {
            try { var g = GK1::Il2Cpp.AppCore.gkj1; return g == null ? -1 : g.GetHpNow(); } catch { return -1; }
        }

        private static string Percent(int hp) => $"{(int)Math.Round(hp * 100.0 / Max)} percent";

        public static void Poll()
        {
            int hp = Now();
            if (hp < 0 || hp > Max) return;
            if (hp != _last)
            {
                // The gauge drains a point at a time: wait until it stops, then say it once.
                if (_last > 0 && hp > 0) { if (_from < 0) _from = _last; _settleAt = UnityEngine.Time.unscaledTime + 0.4f; }
                _last = hp;
            }
            if (_settleAt > 0 && UnityEngine.Time.unscaledTime >= _settleAt)
            {
                _settleAt = -1f;
                if (_from > 0 && hp != _from)
                {
                    Main.Log.Msg($"Truth gauge {_from} -> {hp}");
                    Speech.Say(hp < _from ? $"Wrong. Truth gauge down to {Percent(hp)}." : $"Truth gauge up to {Percent(hp)}.", false, false);
                }
                _from = -1;
            }
        }

        /// <summary>H key.</summary>
        public static void Speak()
        {
            int hp = Now();
            Speech.Say(hp < 0 ? "No truth gauge right now." : $"Truth gauge {Percent(hp)}.", true, false);
        }
    }
}
