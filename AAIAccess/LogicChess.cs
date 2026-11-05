using System;
using Il2CppGK_Common.UI;
using Il2CppGK_Common.UI.LogicChess;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>Second game: logic chess (the timed interrogation). Reads the time gauge (H, and when it changes state),
    /// the guide text, and which chess piece each "examine" choice is.</summary>
    internal static class LogicChess
    {
        private static LogicChessTimeGauge[] _gauges = new LogicChessTimeGauge[0];
        private static LogicChessGuide[] _guides = new LogicChessGuide[0];
        private static float _nextScan, _nextPoll;
        private static string _lastGuide = "";
        private static LogicChessTimeGauge.GaugeStatus _lastStatus = LogicChessTimeGauge.GaugeStatus.DefineMax;
        private static int _lastBand = -1;

        private static LogicChessTimeGauge Gauge()
        {
            foreach (var g in _gauges)
                try { if (g != null && !g.WasCollected && g.isActiveAndEnabled && g.gameObject.activeInHierarchy) return g; } catch { }
            return null;
        }

        public static bool Active => Gauge() != null;

        /// <summary>Time left, 0 to 100.</summary>
        private static int Percent(LogicChessTimeGauge g)
        {
            try
            {
                float w = g.m_FrontGauge.sizeDelta.x, lo = g.m_MinGaugeWidth, hi = g.m_MaxGaugeWidth;
                if (hi - lo < 1f) return -1;
                return Mathf.Clamp(Mathf.RoundToInt((w - lo) * 100f / (hi - lo)), 0, 100);
            }
            catch { return -1; }
        }

        public static void Poll()
        {
            float now = Time.unscaledTime;
            if (now < _nextPoll) return;
            _nextPoll = now + 0.1f;
            if (now >= _nextScan)
            {
                _nextScan = now + 1f;
                try
                {
                    var g = UnityEngine.Object.FindObjectsOfType<LogicChessTimeGauge>(); _gauges = new LogicChessTimeGauge[g.Length]; for (int i = 0; i < g.Length; i++) _gauges[i] = g[i];
                    var d = UnityEngine.Object.FindObjectsOfType<LogicChessGuide>(); _guides = new LogicChessGuide[d.Length]; for (int i = 0; i < d.Length; i++) _guides[i] = d[i];
                }
                catch { }
            }
            // Guide text (instructions shown at the top during the chess).
            foreach (var gd in _guides)
            {
                try
                {
                    if (gd == null || gd.WasCollected || !gd.isActiveAndEnabled) continue;
                    string t = TextCleaner.Clean(gd.m_TextMeshPro?.text);
                    if (!string.IsNullOrEmpty(t) && t != _lastGuide) { _lastGuide = t; Main.Log.Msg("Chess guide: " + t); Speech.Say(t, false, false); }
                }
                catch { }
            }
            var ga = Gauge();
            if (ga == null) { _lastStatus = LogicChessTimeGauge.GaugeStatus.DefineMax; _lastBand = -1; return; }
            try
            {
                var st = ga.m_GaugeStatus; int p = Percent(ga);
                if (st != _lastStatus)
                {
                    Main.Log.Msg($"Chess time gauge {_lastStatus} -> {st}, {p}%");
                    string say = st switch
                    {
                        LogicChessTimeGauge.GaugeStatus.Damage => "Time lost",
                        LogicChessTimeGauge.GaugeStatus.Recovery => "Time recovered",
                        LogicChessTimeGauge.GaugeStatus.Caution or LogicChessTimeGauge.GaugeStatus.ReduceCaution => "Time running low",
                        _ => null
                    };
                    if (say != null && _lastStatus != LogicChessTimeGauge.GaugeStatus.DefineMax) Speech.Say(p >= 0 ? $"{say}, {p} percent left" : say, false, false);
                    _lastStatus = st;
                }
                // While the clock runs down, a word at each quarter.
                if (p >= 0)
                {
                    int band = p > 75 ? 4 : p > 50 ? 3 : p > 25 ? 2 : p > 10 ? 1 : 0;
                    if (_lastBand >= 0 && band < _lastBand && st != LogicChessTimeGauge.GaugeStatus.Damage)
                        Speech.Say(band == 0 ? "Almost out of time" : $"{p} percent time left", false, false);
                    _lastBand = band;
                }
            }
            catch { }
        }

        /// <summary>H: time left.</summary>
        public static bool SpeakTime()
        {
            var g = Gauge(); if (g == null) return false;
            int p = Percent(g);
            Speech.Say(p < 0 ? "Time gauge shown" : $"Time left: {p} percent", true, false);
            return true;
        }

        /// <summary>The examine choices: each is a chess piece (pawn, knight, ...) with a short text, plus a "stay silent" button.</summary>
        public static string Describe(GKUIButton b)
        {
            try
            {
                var eb = b.TryCast<LogicChessExamineButton>();
                var ch = b.GetComponentInParent<LogicChessExamineChoices>();
                if (eb == null && ch == null) return null;
                string text = Menus.ButtonText(b);
                string piece = null;
                if (eb != null && eb.m_ChessPieces != null)
                    for (int i = 0; i < eb.m_ChessPieces.Length; i++)
                        if (eb.m_ChessPieces[i] != null && eb.m_ChessPieces[i].activeInHierarchy) { piece = ((LogicChessExamineButton.ChessPieces)i).ToString().ToLowerInvariant(); break; }
                if (piece == "none") piece = null;
                if (eb == null && string.IsNullOrEmpty(text)) text = "Stay silent";
                return Menus.Join(text, piece);
            }
            catch { return null; }
        }
    }
}
