using System;
using System.Collections.Generic;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>Writes the game's own errors (exceptions Unity reports) to the mod's log: the game keeps no log of its
    /// own, and an error thrown every frame is what stops parts of the game from running. Each different error is
    /// written once, with how often it has come back.</summary>
    internal static class UnityErrors
    {
        private static Application.LogCallback _cb;   // kept so it isn't collected
        private static readonly Dictionary<string, int> _seen = new Dictionary<string, int>();
        private static float _nextSummary;

        public static void Install()
        {
            try
            {
                _cb = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Application.LogCallback>(new Action<string, string, LogType>(OnLog));
                Application.add_logMessageReceived(_cb);
            }
            catch (Exception e) { Main.Log.Warning("Game error log: " + e.Message); }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            try
            {
                if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
                string key = (condition ?? "") + "\n" + (stackTrace ?? "");
                if (_seen.TryGetValue(key, out int n)) { _seen[key] = n + 1; return; }
                if (_seen.Count >= 40) return;
                _seen[key] = 1;
                Main.Log.Warning($"Game {type}: {condition}\n{stackTrace}");
            }
            catch { }
        }

        /// <summary>Now and then: how often each error has come back (one thrown every frame counts up fast).</summary>
        public static void Poll()
        {
            float now = Time.unscaledTime;
            if (now < _nextSummary) return;
            _nextSummary = now + 10f;
            foreach (var kv in _seen)
                if (kv.Value > 1)
                {
                    string first = kv.Key; int nl = first.IndexOf('\n'); if (nl > 0) first = first.Substring(0, nl);
                    Main.Log.Msg($"Game error seen {kv.Value} times so far: {first}");
                }
        }
    }
}
