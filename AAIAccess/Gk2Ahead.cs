extern alias GK2;
using System;
using System.Collections.Generic;
using System.Text;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: the whole page the game is still typing, read straight from the game's script so it can
    /// be spoken at once (as Gk1Ahead does for the first game).
    /// The script is read by cScenario.mScript[k].analyzer: the read position is mStack.end() (dataId, label, and
    /// length = the position), the text is mScriptData[dataId].getStr(label) (ushort[]). Codes 0xE000-0xEFFF are
    /// commands, each followed by as many arguments as cScriptAnalyzerBase.mInfo says (codes not in that table have
    /// none); 0x0A is a new line. A page ends at a page-clear command (see _pageEnd; any other page-clear code would be
    /// learnt the first time the game runs it), and a new message (E101) or window command (E100) also starts a new page.</summary>
    internal static class Gk2Ahead
    {
        private static int _diag, _fails;
        // The six page-clear commands, from cScriptAnalyzer.analyze's jump table (code - 0xE100) in the game's code:
        // E102 PageClear, E103 PageClearNC, E104 PageClearNW, E105 PageClearTime, E106 PageClearDummy, E1B1 PageClearKW.
        // E102 and E104 were also seen in play. None takes arguments.
        private static readonly HashSet<int> _pageEnd = new HashSet<int> { 0xE102, 0xE103, 0xE104, 0xE105, 0xE106, 0xE1B1 };
        private static Dictionary<int, int> _args;

        // The last script split into pages: (start, end, cleaned text, letters only).
        private static string _key = "";
        private static readonly List<(int start, int end, string text, string norm)> _pages = new List<(int, int, string, string)>();

        /// <summary>Hooks the game's page-clear commands, as a safety net: logs and learns any page-end code not in _pageEnd.</summary>
        public static void Apply(HarmonyLib.Harmony h)
        {
            string[] names = { "analyzeCmdPageClear", "analyzeCmdPageClearNC", "analyzeCmdPageClearNW",
                               "analyzeCmdPageClearTime", "analyzeCmdPageClearDummy", "analyzeCmdPageClearKW" };
            var pre = new HarmonyLib.HarmonyMethod(typeof(Gk2Ahead), nameof(Learn));
            foreach (var n in names)
            {
                try
                {
                    var m = HarmonyLib.AccessTools.Method(typeof(G2.cScriptAnalyzer), n + "_Impl", new[] { typeof(ushort), typeof(uint) });
                    if (m == null) { Main.Log.Warning("GK2 read-ahead: no " + n); continue; }
                    h.Patch(m, prefix: pre);
                }
                catch (Exception e) { Main.Log.Warning($"GK2 read-ahead: hook {n} failed: {e.Message}"); }
            }
        }

        private static void Learn(ushort code)
        {
            try
            {
                if (_pageEnd.Add(code)) { Main.Log.Msg($"GK2 read-ahead: learnt page-end code {code:X4}"); _key = ""; }
            }
            catch { }
        }

        private static int Args(int code)
        {
            if (_args == null)
            {
                var d = new Dictionary<int, int>();
                var t = G2.cScriptAnalyzerBase.mInfo;
                if (t != null) for (int i = 0; i < t.Length; i++) d[t[i].code] = t[i].num;
                if (d.Count < 10) return 0;   // table not ready yet: try again next time
                _args = d;
            }
            return _args.TryGetValue(code, out int n) ? n : 0;
        }

        /// <summary>Splits one script text into pages.</summary>
        private static void Split(ushort[] s)
        {
            _pages.Clear();
            var sb = new StringBuilder();
            int start = 0;
            void Close(int end)
            {
                string text = TextCleaner.Clean(sb.ToString());
                if (text.Length > 0) _pages.Add((start, end, text, Gk1Ahead.Norm(text)));
                sb.Clear();
                start = end + 1;
            }
            for (int i = 0; i < s.Length && _pages.Count < 400;)
            {
                int c = s[i];
                if (c == 0) { Close(i); break; }
                if (c >= 0xE000 && c <= 0xEFFF)
                {
                    if (_pageEnd.Contains(c) || c == 0xE100 || c == 0xE101) Close(i);
                    i += 1 + Args(c);
                    continue;
                }
                if (c == 0x0A) sb.Append('\n');
                else if (c >= 0x20) sb.Append((char)c);
                i++;
            }
            Close(s.Length);
        }

        /// <summary>The whole page the window is showing (cleaned), when the game is still typing it; "" when the
        /// window already shows all of it; null if it can't be told for sure.</summary>
        public static string Page(string typed)
        {
            try
            {
                string t = Gk1Ahead.Norm(typed);
                if (t.Length < 2) return null;
                var sc = G2.cScenario.Instance; var arr = sc?.mScript; if (arr == null) return null;
                for (int k = 0; k < arr.Length; k++)
                {
                    var a = arr[k]?.analyzer; if (a == null) continue;
                    var st = a.mStack; if (st == null || st.getNum() <= 0) continue;
                    var top = st.end(); if (top == null) continue;
                    var sn = G2.cScriptAnalyzerBase.mStaticNameData;
                    var data = top.dataId == uint.MaxValue ? (sn != null && sn.Length > 0 ? sn[0] : null)
                        : (a.mScriptData != null && top.dataId < a.mScriptData.Length ? a.mScriptData[(int)top.dataId] : null);
                    var str = data?.getStr(top.label); if (str == null) continue;
                    int pos = top.length;
                    string key = $"{k}/{top.dataId}/{top.label}/{str.Length}/{str.Pointer}";
                    if (key != _key)
                    {
                        var s = new ushort[str.Length];
                        for (int i = 0; i < s.Length; i++) s[i] = str[i];
                        Split(s);
                        _key = key;
                    }
                    // The page the game is reading now, if it starts with what has been typed.
                    foreach (var pg in _pages)
                    {
                        if (pos < pg.start || pos > pg.end + 1) continue;
                        if (!pg.norm.StartsWith(t, StringComparison.Ordinal)) break;
                        return pg.norm.Length > t.Length ? pg.text : "";
                    }
                }
                if (_fails++ < 15) Main.Log.Msg($"GK2 read-ahead: no page starts with '{typed}'");
            }
            catch (Exception e) { if (_diag++ < 5) Main.Log.Warning("GK2 read-ahead: " + e.Message); }
            return null;
        }
    }
}
