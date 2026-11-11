extern alias GK1;
using System;
using System.Text;
using G1 = GK1::Il2Cpp;

namespace AAIAccess
{
    /// <summary>First game (and the demo): the rest of the page the game is still typing, read straight from the
    /// game's script so the whole line can be spoken at once. The script is a list of characters (Unicode), with
    /// 0x0A for a new line and 0xFFFFFF followed by a command number and its arguments; commands 3 and 4 end the page.
    /// Command lengths are those the game's own GKJ1_Font.GetLineNum uses to step over them.</summary>
    internal static class Gk1Ahead
    {
        private const int Cmd = 0xFFFFFF;
        private static int _diag, _fails, _count;
        private static IntPtr _list;
#if DEVTOOLS
        private static IntPtr _dumped; private static int _dumps;
        /// <summary>GKJ1 field +0x4D4: how many letters of the page have been typed (-1 once the page is complete);
        /// GKJ1.EventMsgNVBT passes it to ObjFontStrDspM as fontmax and adds 1 each step.</summary>
        private static unsafe int TypedCount(G1.GKJ1 g)
        {
            try { return *(int*)((byte*)g.Pointer + 0x4D4); } catch { return -2; }
        }
#endif
        private static readonly System.Collections.Generic.List<(string text, string norm)> _pages = new System.Collections.Generic.List<(string, string)>();

        /// <summary>How far to step from a command number to what follows it.</summary>
        private static int Len(int c)
        {
            switch (c)
            {
                case 0x0D: case 0x1B: case 0x1E: return 1;
                case 6: case 8: case 9: case 0x21: case 0x22: case 0x23: case 0x24: return 3;
                case 0x10: case 0x18: case 0x25: return 4;
                case 0x0E: case 0x17: return 5;
                case 0x0C: case 0x13: return 6;
                case 0x0F: return 10;
                case 0x11: return 15;
                case 2: case 5: case 7: case 0x0A: case 0x0B: case 0x14: case 0x15: case 0x16: case 0x19: case 0x1A:
                case 0x1C: case 0x1D: case 0x1F: case 0x20: case 0x26: case 0x27: case 0x28: case 0x29: return 2;
                default: return 1;
            }
        }

        /// <summary>The text from index a up to the end of the page (or index b).</summary>
        private static string Decode(Il2CppSystem.Collections.Generic.List<int> s, int a, int b, out int end)
        {
            var sb = new StringBuilder();
            int n = Math.Min(s.Count, b);
            int i = a;
            for (; i < n && sb.Length < 2000;)
            {
                int v = s[i];
                if (v == Cmd)
                {
                    if (i + 1 >= s.Count) break;
                    int c = s[i + 1];
                    if (c == 3 || c == 4) { end = i; return sb.ToString(); }
                    i += 1 + Len(c);
                    continue;
                }
                if (v == 0x0A) sb.Append('\n');
                else if (v == 0xFF3F) sb.Append('　');
                else if (v >= 0x20 && v < 0x10000) sb.Append((char)v);
                i++;
            }
            end = i;
            return sb.ToString();
        }

        internal static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s ?? "") if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        /// <summary>The whole page the window is showing (cleaned), when the game is still typing it; "" when the
        /// window already shows all of it; null if it can't be told for sure. The message being shown is the game's
        /// event_msgm (what GKJ1.EventMsgNVBT types out each frame): split it into pages and find the one page that
        /// starts with the text typed so far.</summary>
        public static string Page(string typed)
        {
            try
            {
                var g = G1.AppCore.gkj1;
                var s = g?.event_msgm;
                if (s == null) return null;
                string t = Norm(typed);
                if (t.Length < 2) return null;
                int count = s.Count;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                bool rebuilt = false;
                if (s.Pointer != _list || count != _count || attempt == 1)
                {
                    if (s.Pointer != _list || count != _count) Main.Log.Msg($"GK1 read-ahead: new message ({count} codes)");
                    rebuilt = true;
                    // A new message: split it into pages once.
                    _list = s.Pointer; _count = count; _pages.Clear();
                    for (int start = 0; start < count && _pages.Count < 200;)
                    {
                        string page = TextCleaner.Clean(Decode(s, start, int.MaxValue, out int end));
                        if (page.Length > 0) _pages.Add((page, Norm(page)));
                        start = end + 2;
                    }
                }
                // More than one page starting the same way: wait until more is typed.
                string found = null; int matches = 0;
                foreach (var pg in _pages)
                    if (pg.norm.StartsWith(t, StringComparison.Ordinal)) { matches++; found = pg.norm.Length > t.Length ? pg.text : ""; }
                if (matches == 1) return found;
                if (matches > 1) return null;
                if (!rebuilt) continue;   // the list may have been refilled with a new message
                if (_fails++ < 15) Main.Log.Msg($"GK1 read-ahead: no page starts with '{typed}'");
#if DEVTOOLS
                // Once per message: what the pages look like, to see why nothing matched.
                if (_dumped != _list && _dumps++ < 20)
                {
                    _dumped = _list;
                    var sb = new StringBuilder();
                    sb.Append($"GK1 read-ahead: message has {_pages.Count} page(s), {count} codes, typing at {TypedCount(g)}:");
                    foreach (var pg in _pages) sb.Append(" [" + (pg.text.Length > 40 ? pg.text.Substring(0, 40) : pg.text) + "]");
                    var raw = new StringBuilder();
                    for (int i = 0; i < count && i < 80; i++) { int v = s[i]; raw.Append(v >= 0x20 && v < 0x10000 && v != Cmd ? ((char)v).ToString() : $"<{v:X}>"); }
                    Main.Log.Msg(sb.ToString());
                    Main.Log.Msg("GK1 read-ahead: message starts " + raw);
                }
#endif
                return null;
                }
            }
            catch (Exception e) { if (_diag++ < 5) Main.Log.Warning("GK1 read-ahead: " + e.Message); }
            return null;
        }
    }
}
