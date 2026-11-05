extern alias GK2;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: the security video screen (cSceneMovieMG). Modelled on the Phoenix Wright accessibility mod's
    /// video tape support: says when the video plays or pauses and at which frame, says when something can be pointed at,
    /// and while paused [ and ] put the cursor on each thing the game will react to in this frame.
    /// The game's own table of answers (mgmCorrectAnswer, wrong ones included) lists, per frame range, a box or circle.</summary>
    internal static class Gk2Video
    {
        private struct Target { public int Ans, Cx, Cy, Sx, Sy; public bool Circle; public int From, To; }

        private static bool _active, _wasPaused, _wasZoom, _hadTargets;
        private static int _index = -1, _diag, _lastFrame = -1, _wasArea = -1;
        private static float _next, _lostAt = -1;

        // Cursor mapping: the game's answer check uses its own 0..255 coordinates (mPrm.cx/cy); the cursor lives in
        // screen area coordinates. Worked out from two cursor positions.
        private static bool _calibrated;
        private static float _ax = 1, _bx, _ay = 1, _by;
        private static int _calStep; private static float _calAt; private static Vector2 _c1, _p1, _c2;
        private static Target _pending; private static bool _hasPending;

        public static bool Active => _active;

        private static G2.cSceneMovieMG Scene { get { try { return G2.cSceneMovieMG.Instance; } catch { return null; } } }

        private static bool Open(G2.cSceneMovieMG s)
        {
            try { var c = s.m_GKMovieCtrl; return c != null && !c.WasCollected && c.gameObject.activeInHierarchy; } catch { return false; }
        }

        /// <summary>The video's current frame, from the movie player itself (the scene's own counter stays at 0).</summary>
        private static int Frame(G2.cSceneMovieMG s)
        {
            try { var mc = G2.GKJ2Global.MovieCtrl; var pl = mc?.player(); if (pl != null) return (int)pl.getCurrentFrame(); } catch { }
            try { return s.mFrame; } catch { return -1; }
        }
        private static bool Paused(G2.cSceneMovieMG s) { try { return s.IsPause(); } catch { return false; } }
        private static bool Zoomed(G2.cSceneMovieMG s) { try { return s.IsZoom(); } catch { return false; } }

        private static Vector2 CursorXY(G2.cSceneMovieMG s)
        {
            try { return new Vector2(Marshal.ReadInt16(s.Pointer + 0xae), Marshal.ReadInt16(s.Pointer + 0xb0)); } catch { return Vector2.zero; }
        }

        private static Vector2 PrmXY(G2.cSceneMovieMG s)
        {
            // The point the game tests against its answer boxes (it copies these into mPrm.cx/cy when you examine).
            try { return new Vector2(s.mPX & 0xff, s.mPY & 0xff); } catch { return new Vector2(-1, -1); }
        }

        /// <summary>The answer boxes the game checks right now (same choice of table as chkCorrectBox).</summary>
        // The answer tables never change: read each one once (reading it through the game is slow).
        private static readonly Dictionary<int, List<Target>> _tables = new Dictionary<int, List<Target>>();

        /// <summary>Which part of the video is on screen: 0-3 the four zoomed corners, 4 the whole picture.
        /// (The game stores this in mPrm.area only when you examine, so it is read live from the zoom state.)</summary>
        private static int Area(G2.cSceneMovieMG s)
        {
            try { if (s.IsZoom()) { int a = (int)s.mArea; if (a >= 0 && a < 4) return a + 1; } } catch { }
            return 0;
        }

        // The game keeps one answer list per video clip (MOVIE_ID): the whole picture, then the four zoomed corners
        // in the order upper left, upper right, lower left, lower right. Area() gives the offset from the whole clip.
        private static readonly string[] AreaNames = { "the whole picture", "the upper left", "the upper right", "the lower left", "the lower right" };

        private static int BaseId(G2.cSceneMovieMG s) { int id = TryId(s); return id < 0 ? 0 : id / 5 * 5; }

        private static int Mode(G2.cSceneMovieMG s) { try { return Marshal.ReadInt32(s.Pointer + 0x84); } catch { return -1; } }

        private static List<Target> AllTargets(G2.cSceneMovieMG s) => Table(s, Area(s));

        private static List<Target> Table(G2.cSceneMovieMG s, int area)
        {
            int mode = Mode(s);
            int movie = BaseId(s) + area;
            int key = mode * 100 + movie;
            if (_tables.TryGetValue(key, out var cached)) return cached;
            var list = new List<Target>();
            try
            {
                var outer = G2.cSceneMovieMG.mgmCorrectAnswer;
                if (outer != null && mode >= 0 && mode < outer.Count)
                {
                    var row = outer[mode];
                    if (row != null && movie >= 0 && movie < row.Count)
                    {
                        var info = row[movie];
                        for (int i = 0; info != null && i < info.tbl.Count; i++)
                        {
                            var c = info.tbl[i];
                            list.Add(new Target { Ans = c.ansNum, Cx = c.cx, Cy = c.cy, Sx = c.sx, Sy = c.sy, Circle = c.type != 0, From = c.frmStart, To = c.frmEnd });
                        }
                    }
                }
                Main.Log.Msg($"GK2 video table mode={mode} movie={movie} ({AreaNames[area]}): " + string.Join("; ", list.ConvertAll(t => $"ans{t.Ans} f{t.From}-{t.To} {(t.Circle ? "circle" : "box")} {t.Cx},{t.Cy} {t.Sx}x{t.Sy}")));
            }
            catch (Exception e) { if (_diag++ < 10) Main.Log.Warning("GK2 video table: " + e.Message); }
            _tables[key] = list;
            return list;
        }

        private static int TryId(G2.cSceneMovieMG s) { try { return (int)s.mID; } catch { return 0; } }

        /// <summary>Which clip the game's answer check will use (mPrm.id[mMovAccIx]), for the log.</summary>
        private static string Ids(G2.cSceneMovieMG s)
        {
            try
            {
                int ix = Marshal.ReadByte(s.Pointer + 0x89); var ids = s.mPrm.id; var sb = new System.Text.StringBuilder($"accIx={ix} ids=");
                for (int i = 0; ids != null && i < ids.Length; i++) sb.Append((int)ids[i]).Append(',');
                return sb.ToString();
            }
            catch (Exception e) { return "ids?" + e.Message; }
        }

        private static List<Target> TargetsNow(G2.cSceneMovieMG s)
        {
            int f = Frame(s);
            return AllTargets(s).FindAll(t => t.From <= f && f <= t.To);
        }

        public static void Poll()
        {
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.05f;
            var s = Scene;
            bool open = s != null && Open(s);
            if (!open)
            {
                if (_active) { if (_lostAt < 0) _lostAt = now; else if (now - _lostAt > 0.8f) { _active = false; string r = ""; try { r = $" result={s?.mResult} success={s?.mSuccess} prm.cx={s?.mPrm.cx} prm.cy={s?.mPrm.cy} prm.frame={s?.mPrm.frame}"; } catch { } Main.Log.Msg("GK2 video closed" + r); } }
                return;
            }
            _lostAt = -1;
            try
            {
                if (!_active)
                {
                    _active = true; _index = -1; _wasPaused = Paused(s); _wasZoom = Zoomed(s); _hadTargets = false; _calibrated = false; _calStep = 0;
                    Main.Log.Msg($"GK2 video open: id={s.mID} mode={s.mMode} frame={Frame(s)} area={s.m_AreaX},{s.m_AreaY},{s.m_AreaW},{s.m_AreaH} cursor={CursorXY(s)} prm={PrmXY(s)}");
                    for (int a = 0; a <= 4; a++) Table(s, a);
                    Main.Log.Msg("GK2 video " + Ids(s));   // read the answer tables now, not in the middle of playing
                    Speech.Say("Security video. Play or pause with the game's keys; while paused, the bracket keys move to each thing you can point at. F3 for the frame.", false, false);
                }
                bool paused = Paused(s), zoom = Zoomed(s);
                int f = Frame(s);
                if (paused != _wasPaused)
                {
                    _wasPaused = paused; _index = -1;
                    if (paused) { int n = TargetsNow(s).Count; Speech.Say(Menus.Join($"Paused at frame {f}", n == 0 ? null : n == 1 ? "1 thing to point at" : $"{n} things to point at"), true, false); }
                    else Speech.Say("Playing", true, false);
                }
                int areaNow = Area(s);
                if (zoom != _wasZoom || (zoom && areaNow != _wasArea)) { _wasZoom = zoom; _wasArea = areaNow; _index = -1; _calibrated = false; Speech.Say(zoom ? "Zoomed in on " + AreaNames[Area(s)] : "Zoomed out", true, false); Main.Log.Msg($"GK2 video zoom: area {Area(s)}"); }
                // While it plays: a word when something can be pointed at, like the Phoenix Wright mod.
                if (!paused && f != _lastFrame)
                {
                    bool has = TargetsNow(s).Count > 0;
                    if (has && !_hadTargets && !TargetsNow(s).TrueForAll(t => t.From <= 0 && t.To >= 9999)) { Main.Log.Msg($"GK2 video: something to point at from frame {f}"); Speech.Say("Something to point at. Pause now.", true, false); }
                    _hadTargets = has;
                }
                _lastFrame = f;
                Calibrate(s, now);
                if (_verifyAt > 0 && now >= _verifyAt)
                {
                    _verifyAt = -1;
                    var pr = PrmXY(s); int got = -1;
                    try { got = s.chkCorrectBox((int)pr.x, (int)pr.y, null); } catch (Exception e) { if (_diag++ < 30) Main.Log.Warning("chkCorrectBox: " + e.Message); }
                    // Our own reading of the same table, in case the game's check can't be called from here.
                    int mine = -1; int fr = Frame(s);
                    foreach (var t in AllTargets(s))
                    {
                        if (fr < t.From || fr > t.To) continue;
                        bool hit = t.Circle ? (pr.x - t.Cx) * (pr.x - t.Cx) + (pr.y - t.Cy) * (pr.y - t.Cy) <= t.Sx * t.Sx
                                            : Math.Abs(pr.x - t.Cx) <= t.Sx && Math.Abs(pr.y - t.Cy) <= t.Sy;
                        if (hit) { mine = t.Ans; break; }
                    }
                    var cur = CursorXY(s);
                    Main.Log.Msg($"GK2 video check at frame {fr}: cursor {cur}, check point {pr}, game says {got}, table says {mine}, {Ids(s)}");
                    if (got < 0 && mine < 0 && _nudges < 4)
                    {
                        _nudges++;
                        s.SetCursor((int)Math.Round(cur.x + (_verify.Cx - pr.x)), (int)Math.Round(cur.y + (_verify.Cy - pr.y)));
                        _verifyAt = now + 0.2f;
                    }
                    else if (got < 0 && mine < 0) Speech.Say("The cursor couldn't reach it.", false, false);
                    else Speech.Say("On it. Examine now.", false, false);
                }
            }
            catch (Exception e) { if (_diag++ < 20) Main.Log.Warning("GK2 video poll: " + e.Message); }
        }

        // ---------- moving the cursor ----------

        private static void Calibrate(G2.cSceneMovieMG s, float now)
        {
            if (_calStep == 0 || now < _calAt) return;
            float x0 = s.m_AreaX + s.m_AreaW * 0.25f, y0 = s.m_AreaY + s.m_AreaH * 0.25f, x1 = s.m_AreaX + s.m_AreaW * 0.75f, y1 = s.m_AreaY + s.m_AreaH * 0.75f;
            if (_calStep == 1) { _c1 = new Vector2(x0, y0); s.SetCursor((int)x0, (int)y0); _calStep = 2; _calAt = now + 0.15f; return; }
            if (_calStep == 2) { _p1 = PrmXY(s); _c2 = new Vector2(x1, y1); s.SetCursor((int)x1, (int)y1); _calStep = 3; _calAt = now + 0.15f; return; }
            if (_calStep == 3)
            {
                var p2 = PrmXY(s);
                _calStep = 0;
                if (Math.Abs(p2.x - _p1.x) > 1 && Math.Abs(p2.y - _p1.y) > 1)
                {
                    _ax = (_c2.x - _c1.x) / (p2.x - _p1.x); _bx = _c1.x - _ax * _p1.x;
                    _ay = (_c2.y - _c1.y) / (p2.y - _p1.y); _by = _c1.y - _ay * _p1.y;
                    _calibrated = true;
                }
                else
                {
                    // The game's coordinates didn't follow: assume the cursor area maps straight onto 256 by 192.
                    _ax = s.m_AreaW / 256f; _bx = s.m_AreaX; _ay = s.m_AreaH / 192f; _by = s.m_AreaY; _calibrated = true;
                }
                Main.Log.Msg($"GK2 video cursor map: prm {_p1} at {_c1}, prm {p2} at {_c2}; x = {_ax:0.###}*cx + {_bx:0.#}, y = {_ay:0.###}*cy + {_by:0.#}");
                if (_hasPending) { _hasPending = false; MoveTo(s, _pending); }
            }
        }

        private static void MoveTo(G2.cSceneMovieMG s, Target t)
        {
            int x = (int)Math.Round(_ax * t.Cx + _bx), y = (int)Math.Round(_ay * t.Cy + _by);
            s.SetCursor(x, y);
            Main.Log.Msg($"GK2 video cursor to ans{t.Ans} at {t.Cx},{t.Cy} -> {x},{y}");
            _verifyAt = Time.unscaledTime + 0.2f; _verify = t; _nudges = 0;
        }

        private static float _verifyAt = -1; private static Target _verify; private static int _nudges;

        /// <summary>Where the game itself will accept an answer right now: its own check, tried all over the picture.
        /// One target per answer, at the hit point nearest the middle of that answer's hits.</summary>
        private static List<(int ans, int x, int y)> GameTargets(G2.cSceneMovieMG s)
        {
            var hits = new Dictionary<int, List<Vector2>>();
            for (int y = 0; y < 256; y += 3)
                for (int x = 0; x < 256; x += 3)
                {
                    int r; try { r = s.chkCorrectBox(x, y, null); } catch { return new List<(int, int, int)>(); }
                    if (r < 0) continue;
                    if (!hits.TryGetValue(r, out var l)) hits[r] = l = new List<Vector2>();
                    l.Add(new Vector2(x, y));
                }
            var res = new List<(int, int, int)>();
            foreach (var kv in hits)
            {
                var m = Vector2.zero; foreach (var p in kv.Value) m += p; m /= kv.Value.Count;
                Vector2 best = kv.Value[0]; float bd = float.MaxValue;
                foreach (var p in kv.Value) { float d = (p - m).sqrMagnitude; if (d < bd) { bd = d; best = p; } }
                res.Add((kv.Key, (int)best.x, (int)best.y));
            }
            res.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            Main.Log.Msg($"GK2 video game targets at frame {Frame(s)} (zoom {Area(s)}): " + string.Join("; ", res.ConvertAll(t => $"ans{t.Item1} at {t.Item2},{t.Item3}")));
            return res;
        }

        public static void Step(int dir)
        {
            var s = Scene; if (s == null) return;
            try
            {
                if (!Paused(s)) { Speech.Say("Pause the video first.", true, false); return; }
                var list = GameTargets(s);
                if (list.Count == 0)
                {
                    // The game may only switch its live answer list when you examine: use its table for this clip.
                    var seen = new HashSet<int>();
                    foreach (var t0 in TargetsNow(s)) if (seen.Add(t0.Ans)) list.Add((t0.Ans, t0.Cx, t0.Cy));
                    if (list.Count > 0) Main.Log.Msg($"GK2 video table targets at frame {Frame(s)} ({AreaNames[Area(s)]}): " + string.Join("; ", list.ConvertAll(t => $"ans{t.ans} at {t.x},{t.y}")));
                }
                if (list.Count == 0) { Speech.Say($"Nothing to point at in frame {Frame(s)} here. F3 says where and when to stop.", true, false); return; }
                _index = _index < 0 ? (dir > 0 ? 0 : list.Count - 1) : (_index + dir + list.Count) % list.Count;
                var t = list[_index];
                // The game's check point is the cursor position itself.
                s.SetCursor(t.x, t.y);
                _verify = new Target { Ans = t.ans, Cx = t.x, Cy = t.y }; _verifyAt = Time.unscaledTime + 0.25f; _nudges = 0;
                Main.Log.Msg($"GK2 video cursor to game target ans{t.ans} at {t.x},{t.y}");
                Speech.Say(list.Count == 1 ? "Moving to it" : $"Thing {_index + 1} of {list.Count}", true, false);
            }
            catch (Exception e) { Main.Log.Error("GK2 video step: " + e); }
        }

        /// <summary>The stretches of frames where something can be pointed at (in the current zoom), joined up.</summary>
        private static List<(int from, int to)> Spans(G2.cSceneMovieMG s)
        {
            var all = AllTargets(s).ConvertAll(t => (t.From, t.To));
            all.Sort((a, b) => a.From.CompareTo(b.From));
            var spans = new List<(int, int)>();
            foreach (var r in all)
            {
                if (spans.Count > 0 && r.From <= spans[spans.Count - 1].Item2 + 2) { var l = spans[spans.Count - 1]; spans[spans.Count - 1] = (l.Item1, Math.Max(l.Item2, r.To)); }
                else spans.Add((r.From, r.To));
            }
            return spans;
        }

        /// <summary>Like the Phoenix Wright mod's hint: where to stop the video.</summary>
        private static string WhenToStop(G2.cSceneMovieMG s)
        {
            int f = Frame(s), cur = Area(s);
            var parts = new List<string>();
            // This view first, then the others.
            var order = new List<int> { cur }; for (int a0 = 0; a0 <= 4; a0++) if (a0 != cur) order.Add(a0);
            foreach (int a in order)
            {
                var spans = SpansFor(s, a);
                if (spans.Count == 0) continue;
                string where = a == cur ? "in this view" : (a == 0 ? "zoomed out" : "zoomed in on " + AreaNames[a]);
                var sp = spans.Find(x => x.to >= f); if (sp.to == 0 && sp.from == 0) sp = spans[0];
                bool always = sp.from <= 0 && sp.to >= 9999;   // the game writes 0 to 65535 for "the whole video"
                string range = always ? "at any frame" : $"frames {sp.from} to {sp.to}";
                string when = a == cur && f >= sp.from && f <= sp.to ? (always ? "at any frame, pause anywhere" : $"you're in the right place, {range}") : range;
                string item = $"{where}: {when}";
                if (!parts.Contains(item)) parts.Add(item);
            }
            return parts.Count == 0 ? "nothing to point at in this video" : "Something to point at " + string.Join("; ", parts);
        }

        private static List<(int from, int to)> SpansFor(G2.cSceneMovieMG s, int area)
        {
            var all = Table(s, area).ConvertAll(t => (t.From, t.To));
            all.Sort((a, b) => a.From.CompareTo(b.From));
            var spans = new List<(int, int)>();
            foreach (var r in all)
            {
                if (spans.Count > 0 && r.From <= spans[spans.Count - 1].Item2 + 2) { var l = spans[spans.Count - 1]; spans[spans.Count - 1] = (l.Item1, Math.Max(l.Item2, r.To)); }
                else spans.Add((r.From, r.To));
            }
            return spans;
        }

        /// <summary>Development: where the game's own answer check says yes in this frame (scanning its 0-255 space),
        /// and where each field it reads sits in memory.</summary>
        private static void Diagnose(G2.cSceneMovieMG s)
        {
            try
            {
                var sb = new System.Text.StringBuilder("GK2 video game-check scan:");
                int hits = 0, x0 = 999, y0 = 999, x1 = -1, y1 = -1, ans = -1;
                for (int y = 0; y < 256; y += 3)
                    for (int x = 0; x < 256; x += 3)
                    {
                        int r = s.chkCorrectBox(x, y, null);
                        if (r >= 0) { hits++; ans = r; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
                    }
                sb.Append(hits == 0 ? " no hits" : $" {hits} hits, ans{ans}, x {x0}-{x1}, y {y0}-{y1}");
                foreach (var name in new[] { "mMode", "mZoom", "mMovAccIx", "mPX", "mPY", "mPrm", "mResult", "mSuccess", "mArea", "mCMInfo", "mFrame" })
                {
                    try
                    {
                        var f = typeof(G2.cSceneMovieMG).GetField("NativeFieldInfoPtr_" + name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (f == null) { sb.Append($" {name}=?"); continue; }
                        var ptr = (IntPtr)f.GetValue(null);
                        sb.Append($" {name}@0x{Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(ptr):X}");
                    }
                    catch (Exception e) { sb.Append($" {name}!{e.GetType().Name}"); }
                }
                Main.Log.Msg(sb.ToString());
            }
            catch (Exception e) { Main.Log.Warning("GK2 video diagnose: " + e.Message); }
        }

        public static void ReadStatus()
        {
            var s = Scene; if (s == null) return;
            try
            {
                int n = TargetsNow(s).Count;
                Speech.Say(Menus.Join(Paused(s) ? "Paused" : "Playing", $"frame {Frame(s)}", Zoomed(s) ? "zoomed in" : null,
                    n == 0 ? "nothing to point at here" : n == 1 ? "1 thing to point at" : $"{n} things to point at", WhenToStop(s)), true, false);
                Main.Log.Msg($"GK2 video status: frame={Frame(s)} cursor={CursorXY(s)} prm={PrmXY(s)} zoom={s.mZoom} area={Area(s)} mArea={s.mArea} result={s.mResult} success={s.mSuccess} {Ids(s)}");
                Diagnose(s);
            }
            catch (Exception e) { Main.Log.Error("GK2 video status: " + e.Message); }
        }
    }
}
