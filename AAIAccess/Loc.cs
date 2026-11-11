using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AAIAccess
{
    /// <summary>The mod's own words in the game's text language (Options > Language > Text).
    /// Everything the mod speaks goes through Speech, which calls Translate: a sentence is looked up whole in
    /// lang/&lt;code&gt;.txt, or matched against a pattern ("Detail {0} of {1}", the parts translated in turn), or split
    /// into sentences and phrases that are looked up one by one. Text that isn't the mod's (the game's own, already in
    /// the right language) matches nothing and is left exactly as it is.
    /// Data files (picture descriptions and so on) come from lang/&lt;code&gt;/&lt;name&gt; when that exists.</summary>
    internal static class Loc
    {
        // Codes for the game's Languages enum: Japanese, English, French, German, Korean, Traditional_Chinese, Simplified_Chinese.
        private static readonly string[] Codes = { "ja", "en", "fr", "de", "ko", "zht", "zhs" };

        private static string _code = "en";
        private static float _nextCheck;
        /// <summary>Goes up every time the language changes, so data caches know to reload.</summary>
        public static int Version { get; private set; }

        /// <summary>The current text language: en, fr, de, ko, zht, zhs or ja.</summary>
        public static string Code
        {
            get
            {
                float now = 0; try { now = UnityEngine.Time.unscaledTime; } catch { }
                if (now >= _nextCheck)
                {
                    _nextCheck = now + 1f;
                    string c = _code;
                    try
                    {
                        var lm = Il2CppGK_System.Singleton<Il2CppGK_System.Language.LanguageManager>.Instance;
                        if (lm != null)
                        {
                            int l = (int)lm.CurrentLanguage;
                            if (l >= 0 && l < Codes.Length) c = Codes[l];
                        }
                    }
                    catch { }
                    if (c != _code)
                    {
                        _code = c; Version++; _cache.Clear();
                        Main.Log.Msg("Mod language: " + c);
                    }
                }
                return _code;
            }
        }

        private static bool Cjk => _code == "ja" || _code == "zht" || _code == "zhs";

        // ---------- the table ----------
        private class Table
        {
            public readonly Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly List<(Regex re, string to, int n)> Patterns = new List<(Regex, string, int)>();
        }
        private static readonly Dictionary<string, Table> _tables = new Dictionary<string, Table>();
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);

        private static List<string> Lines(string resource)
        {
            var list = new List<string>();
            using var st = typeof(Loc).Assembly.GetManifestResourceStream(resource);
            if (st == null) return null;
            using var rd = new System.IO.StreamReader(st, Encoding.UTF8);
            string l;
            while ((l = rd.ReadLine()) != null) if (l.Trim().Length > 0 && !l.StartsWith("#")) list.Add(l.Trim());
            return list;
        }

        private static Table Get(string code)
        {
            if (_tables.TryGetValue(code, out var t)) return t;
            t = new Table();
            try
            {
                var en = Lines("lang.en.txt");
                var tr = Lines($"lang.{code}.txt");
                if (en != null && tr != null)
                {
                    if (en.Count != tr.Count) Main.Log.Warning($"lang/{code}.txt has {tr.Count} lines, English has {en.Count}: lines may not match");
                    var pats = new List<(Regex, string, int, int)>();
                    for (int i = 0; i < en.Count && i < tr.Count; i++)
                    {
                        string k = en[i], v = tr[i];
                        if (!k.Contains("{")) { t.Exact[k] = v; continue; }
                        // A pattern: {0}, {1} ... become groups; the more fixed text, the earlier it is tried.
                        int n = 0;
                        string re = "^" + Regex.Replace(Regex.Escape(k).Replace("\\{", "{"), @"\{(\d)}", m => { n++; return $"(?<p{m.Groups[1].Value}>.+?)"; }) + "$";
                        int fixedLen = Regex.Replace(k, @"\{\d}", "").Length;
                        pats.Add((new Regex(re, RegexOptions.Compiled | RegexOptions.Singleline), v, n, fixedLen));
                    }
                    pats.Sort((a, b) => b.Item4.CompareTo(a.Item4));
                    foreach (var p in pats) t.Patterns.Add((p.Item1, p.Item2, p.Item3));
                    Main.Log.Msg($"Mod language {code}: {t.Exact.Count} phrases, {t.Patterns.Count} patterns");
                }
            }
            catch (Exception e) { Main.Log.Warning($"lang/{code}: {e.Message}"); }
            _tables[code] = t;
            return t;
        }

        /// <summary>The mod's text in the current language (unchanged for English, and for text the mod doesn't know).</summary>
        public static string Translate(string s)
        {
            if (string.IsNullOrEmpty(s) || Code == "en") return s;
            if (_cache.TryGetValue(s, out var c)) return c;
            string r;
            try { r = Tr(s, Get(_code), 0) ?? s; }
            catch (Exception e) { Main.Log.Warning("Translate: " + e.Message); r = s; }
            if (_cache.Count > 4000) _cache.Clear();
            _cache[s] = r;
            return r;
        }

        /// <summary>Same as Translate; for code that builds text in pieces.</summary>
        public static string T(string s) => Translate(s);

        // Returns null when nothing in s was translated.
        private static string Tr(string s, Table t, int depth)
        {
            if (depth > 6 || s.Length == 0) return null;
            if (t.Exact.TryGetValue(s, out var x)) return x;
            // A phrase given without its full stop (or with one the table doesn't have).
            if (s.EndsWith(".") && t.Exact.TryGetValue(s.Substring(0, s.Length - 1), out x)) return x + Stop();
            if (!s.EndsWith(".") && t.Exact.TryGetValue(s + ".", out x)) return TrimStop(x);

            // Several sentences: each on its own.
            var sent = SplitKeep(s, ". ");
            if (sent.Count > 1) return JoinParts(sent, t, depth, Cjk ? "" : " ");

            // Patterns whose filled-in parts are single phrases (no ", " and no closing full stop) come first, so a
            // pattern doesn't swallow a whole list; then the text is split into phrases; looser matches come last.
            string pat = Pattern(s, t, depth, true);
            if (pat != null) return pat;
            if (s.EndsWith(".") && s.Length > 1)
            {
                string inner = Tr(s.Substring(0, s.Length - 1), t, depth + 1);
                if (inner != null) return TrimStop(inner) + Stop();
            }
            // Phrases: "a, b, c", then "label: value".
            foreach (var sep in new[] { "; ", ", ", ": " })
            {
                var parts = new List<string>(s.Split(new[] { sep }, StringSplitOptions.None));
                if (parts.Count < 2) continue;
                var outp = new List<string>(); bool any = false;
                foreach (var p in parts) { string q = Tr(p, t, depth + 1); if (q != null) any = true; outp.Add(q ?? p); }
                if (!any) break;
                return string.Join(Sep(sep), outp);
            }
            return Pattern(s, t, depth, false);
        }

        private static readonly string[] Breaks = { ", ", "; ", ": ", ". " };
        private static string Pattern(string s, Table t, int depth, bool strict)
        {
            foreach (var (re, to, n) in t.Patterns)
            {
                var m = re.Match(s);
                if (!m.Success) continue;
                if (strict)
                {
                    bool ok = true;
                    for (int i = 0; i < 4 && ok; i++)
                    {
                        var g = m.Groups["p" + i];
                        if (!g.Success) continue;
                        if (g.Value.EndsWith(".")) ok = false;
                        foreach (var b in Breaks) if (g.Value.Contains(b)) ok = false;
                    }
                    if (!ok) continue;
                }
                string res = to;
                for (int i = 0; i < 4; i++)
                {
                    var g = m.Groups["p" + i];
                    if (!g.Success) continue;
                    res = res.Replace("{" + i + "}", Tr(g.Value, t, depth + 1) ?? g.Value);
                }
                return res;
            }
            return null;
        }

        private static string JoinParts(List<string> parts, Table t, int depth, string glue)
        {
            var outp = new List<string>(); bool any = false;
            foreach (var p in parts) { string q = Tr(p, t, depth + 1); if (q != null) any = true; outp.Add(q ?? p); }
            return any ? string.Join(glue, outp) : null;
        }

        /// <summary>Splits after each separator, keeping the full stop with its sentence.</summary>
        private static List<string> SplitKeep(string s, string sep)
        {
            var list = new List<string>();
            int start = 0, i;
            while ((i = s.IndexOf(sep, start, StringComparison.Ordinal)) >= 0)
            {
                list.Add(s.Substring(start, i + 1 - start));
                start = i + sep.Length;
            }
            list.Add(s.Substring(start));
            list.RemoveAll(p => p.Length == 0);
            return list;
        }

        private static string Stop() => Cjk ? "。" : ".";
        private static string TrimStop(string s) => s.TrimEnd('.', '。');
        private static string Sep(string sep)
        {
            if (!Cjk) return sep;
            if (sep == ", ") return _code == "ja" ? "、" : "，";
            if (sep == "; ") return "；";
            if (sep == ": ") return "：";
            return sep;
        }

        // ---------- data files ----------
        /// <summary>A data file (closeups.txt, pagetext.txt, gk2names.txt) in the current language, or the English one.</summary>
        public static System.IO.Stream Resource(string name)
        {
            string c = Code;
            var asm = typeof(Loc).Assembly;
            if (c != "en")
            {
                var st = asm.GetManifestResourceStream($"lang.{c}.{name}");
                if (st != null) return st;
            }
            return asm.GetManifestResourceStream(name);
        }
    }
}
