extern alias GK2;
using System;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: the truth gauge (cScenario.hpgauge). H reads it; drops and rises are announced, and so is
    /// the flashing part the game shows before a risky choice (what a wrong answer would cost).</summary>
    internal static class Gk2Gauge
    {
        private static int _last = -1, _from = -1, _lastDamage = -1;
        private static float _settleAt = -1, _next;

        private static G2.cHpGauge Gauge { get { try { return G2.cScenario.Instance?.hpgauge(); } catch { return null; } } }

        private static float Max { get { try { float m = G2.cHpGauge.MaxHP; return m > 0 ? m : 80f; } catch { return 80f; } } }

        private static string Percent(int hp) => $"{(int)Math.Round(hp * 100.0 / Max)} percent";

        private static bool Shown(G2.cHpGauge g) { try { var u = g.m_Gauge; return u != null && !u.WasCollected && u.gameObject.activeInHierarchy; } catch { return false; } }

        public static bool Available => Gauge != null;

        public static void Poll()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.1f;
            if (Gk2Map.Ctrl == null && G2.cScenario.Instance == null) return;
            var g = Gauge; if (g == null) return;
            int hp, dmg;
            try { hp = g.mHp; dmg = g.mDamageHp; } catch { return; }
            if (hp < 0 || hp > Max * 2) return;
            if (_last < 0) { _last = hp; Main.Log.Msg($"GK2 truth gauge {hp} of {Max}"); }
            if (hp != _last)
            {
                if (_from < 0) _from = _last;
                _last = hp; _settleAt = now + 0.5f;
            }
            if (_settleAt > 0 && now >= _settleAt)
            {
                _settleAt = -1;
                if (_from >= 0 && hp != _from)
                {
                    Main.Log.Msg($"GK2 truth gauge {_from} -> {hp}");
                    Speech.Say(hp < _from ? $"Truth gauge down to {Percent(hp)}." : $"Truth gauge up to {Percent(hp)}.", false, false);
                }
                _from = -1;
            }
            // The penalty shown in advance (flashing part of the gauge).
            bool shown = Shown(g);
            int d = shown ? dmg : 0;
            if (d != _lastDamage)
            {
                if (d > 0 && _lastDamage >= 0) Main.Log.Msg($"GK2 gauge penalty shown: {d}");
                _lastDamage = d;
            }
        }

        /// <summary>H key. False when there is no gauge (first game).</summary>
        public static bool Speak()
        {
            var g = Gauge; if (g == null) return false;
            int hp; try { hp = g.mHp; } catch { return false; }
            Speech.Say($"Truth gauge {Percent(hp)}.", true, false);
            return true;
        }
    }
}
