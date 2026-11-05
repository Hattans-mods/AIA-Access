extern alias GK2;
using System;
using System.Collections.Generic;
using UnityEngine;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: the close-up picture you examine with a cursor ("upcut" in the game's code).
    /// [ and ] move the cursor to each spot, a tone plays when the cursor is over one, F3 says where it is.
    /// The spots come from the picture's own check areas (cUpcutCtrl.coll()); the cursor is a sub-screen task.</summary>
    internal static class Gk2Examine
    {
        private class Spot { public uint Label; public Vector2 Aim; public List<(Vector2 a, Vector2 b, string shape)> Rects = new List<(Vector2, Vector2, string)>(); public string Name; public bool Hidden; }

        private static readonly List<Spot> _spots = new List<Spot>();
        private static bool _active;
        private static string _pic = "";
        private static int _index = -1;
        private static int _lastOver = -2;
        private static float _arriveAt = -1, _lostAt = -1, _stepAt;
        private static Vector2 _lastPos;
        private static int _diag;

        public static bool Active => _active;

        private static G2.cSubscrnTaskCheckCursor Cursor()
        {
            try
            {
                var sc = G2.cSubscrnCtrl.Instance; if (sc == null) return null;
                foreach (var n in new[] { G2.GKJ2Global.SUBSCRN_TASK_NAME.SUBSCRN_TASK_NAME_CHECK_CURSOR, G2.GKJ2Global.SUBSCRN_TASK_NAME.SUBSCRN_TASK_NAME_CHECK_CURSOR_00, G2.GKJ2Global.SUBSCRN_TASK_NAME.SUBSCRN_TASK_NAME_CHECK_CURSOR_01 })
                {
                    var t = sc.search_Impl(n);
                    var c = t?.TryCast<G2.cSubscrnTaskCheckCursor>();
                    if (c != null) return c;
                }
                int k = 0;
                for (var t = sc.getFirst(); t != null && k < 200; t = sc.getNext(t), k++)
                {
                    var c = t.TryCast<G2.cSubscrnTaskCheckCursor>();
                    if (c != null) return c;
                }
            }
            catch { }
            return null;
        }

        private static bool Shown(G2.cSubscrnTaskCheckCursor c)
        {
            try { var o = c?.m_GKCursorObj; return o != null && !o.WasCollected && o.m_IsDisplayed; } catch { return false; }
        }

        private static bool OnChecked(G2.cSubscrnTaskCheckCursor c)
        {
            try { var o = c.m_GKCursorObj; return o.m_CheckMarkObj != null && o.m_CheckMarkObj.activeInHierarchy; } catch { return false; }
        }

        private static Vector2 CurPos(G2.cSubscrnTaskCheckCursor c) => new Vector2(Gk2Map.Fx(c.mPos.x), Gk2Map.Fx(c.mPos.y));

        /// <summary>The picture scrolls: spots are in picture coordinates, the cursor in screen ones.</summary>
        private static Vector2 Scroll()
        {
            try { var b = Gk2Map.Upcut?.bg(); if (b != null) { var p = b.pos(); return new Vector2(Gk2Map.Fx(p.x), Gk2Map.Fx(p.y)); } } catch { }
            return Vector2.zero;
        }

        private static bool InRect((Vector2 a, Vector2 b, string shape) r, Vector2 p)
        {
            float x0 = Math.Min(r.a.x, r.b.x), x1 = Math.Max(r.a.x, r.b.x), y0 = Math.Min(r.a.y, r.b.y), y1 = Math.Max(r.a.y, r.b.y);
            if (p.x < x0 || p.x > x1 || p.y < y0 || p.y > y1) return false;
            float u = (p.x - x0) / Math.Max(1, x1 - x0), v = (p.y - y0) / Math.Max(1, y1 - y0);
            switch (r.shape)
            {
                case "MAP_COLL_SHAPE_TRI_TL": return u + v <= 1;
                case "MAP_COLL_SHAPE_TRI_TR": return (1 - u) + v <= 1;
                case "MAP_COLL_SHAPE_TRI_BL": return u + (1 - v) <= 1;
                case "MAP_COLL_SHAPE_TRI_BR": return (1 - u) + (1 - v) <= 1;
                case "MAP_COLL_SHAPE_DIAMOND": return Math.Abs(u - 0.5f) + Math.Abs(v - 0.5f) <= 0.5f;
            }
            return true;
        }

        // Every area in the game's own order: where areas overlap, the game takes the first one that holds the cursor.
        private static readonly List<((Vector2 a, Vector2 b, string shape) r, Spot s)> _order = new List<((Vector2, Vector2, string), Spot)>();

        private static int SpotAt(Vector2 picPos)
        {
            foreach (var o in _order) if (InRect(o.r, picPos)) return _spots.IndexOf(o.s);
            return -1;
        }

        private static void Build()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { Build0(); } finally { Main.Log.Msg($"GK2 close-up data took {sw.ElapsedMilliseconds} ms"); }
        }

        private static void Build0()
        {
            var mk = System.Diagnostics.Stopwatch.StartNew(); var marks = new List<string>();
            void Mark(string n) { if (mk.ElapsedMilliseconds > 30) marks.Add($"{n} {mk.ElapsedMilliseconds} ms"); mk.Restart(); }
            _spots.Clear(); _order.Clear(); _index = -1; _lastOver = -2; _arriveAt = -1; _scrollTo = new Vector2(-1, -1); _scrolls = 0;
            var u = Gk2Map.Upcut; if (u == null) return;
            try { _pic = u.bg().getUpcutID().ToString(); } catch { _pic = ""; }
            try
            {
                var t0 = System.Diagnostics.Stopwatch.StartNew();
                var co = u.coll();
                var chk = co.getCheckCollTbl(); int nc = co.getCheckCollNum();
                if (t0.ElapsedMilliseconds > 30) Main.Log.Msg($"GK2 close-up: reading the spot table took {t0.ElapsedMilliseconds} ms");
                for (int i = 0; i < nc && chk != null && i < chk.Length; i++)
                {
                    var c = chk[i]; if (c == null) continue;
                    bool on = true; try { on = co.isCollEnable(c.RefFlagNo); } catch { }
                    if (!on) continue;
                    var s = _spots.Find(x => x.Label == c.ScriptLabelNo);
                    if (s == null) { s = new Spot { Label = c.ScriptLabelNo }; _spots.Add(s); }
                    var rr = (new Vector2(Gk2Map.Fx(c.Start.x), Gk2Map.Fx(c.Start.y)), new Vector2(Gk2Map.Fx(c.End.x), Gk2Map.Fx(c.End.y)), c.Shape.ToString());
                    s.Rects.Add(rr); _order.Add((rr, s));
                }
            }
            catch (Exception e) { Main.Log.Warning("GK2 upcut spots: " + e.Message); }
            Mark("spot table");
            foreach (var s in _spots)
            {
                // Aim at the middle of the biggest plain rectangle (or the solid corner of a triangle).
                float best = -1;
                foreach (var r in s.Rects)
                {
                    float area = Math.Abs((r.b.x - r.a.x) * (r.b.y - r.a.y)) * (r.shape.EndsWith("RECT", StringComparison.Ordinal) ? 1f : 0.5f);
                    if (area <= best) continue;
                    best = area;
                    Vector2 mid = (r.a + r.b) / 2;
                    if (!r.shape.EndsWith("RECT", StringComparison.Ordinal))
                    {
                        float x0 = Math.Min(r.a.x, r.b.x), x1 = Math.Max(r.a.x, r.b.x), y0 = Math.Min(r.a.y, r.b.y), y1 = Math.Max(r.a.y, r.b.y);
                        float cx = r.shape.Contains("L") && !r.shape.EndsWith("TRI_TR", StringComparison.Ordinal) && !r.shape.EndsWith("TRI_BR", StringComparison.Ordinal) ? x0 + (x1 - x0) / 3 : x1 - (x1 - x0) / 3;
                        float cy = r.shape.EndsWith("TRI_TL", StringComparison.Ordinal) || r.shape.EndsWith("TRI_TR", StringComparison.Ordinal) ? y0 + (y1 - y0) / 3 : y1 - (y1 - y0) / 3;
                        mid = new Vector2(cx, cy);
                    }
                    s.Aim = mid;
                }
                s.Name = Gk2Nav.LookupName(_pic + ":" + s.Label);
            }
            Mark("names");
            // Left to right, then top to bottom.
            // Aim where the game will really pick this spot: a point of it not covered by an earlier area, near the middle of those points.
            foreach (var sp in _spots)
            {
                var pts = new List<Vector2>();
                foreach (var r in sp.Rects)
                {
                    float x0 = Math.Min(r.a.x, r.b.x), x1 = Math.Max(r.a.x, r.b.x), y0 = Math.Min(r.a.y, r.b.y), y1 = Math.Max(r.a.y, r.b.y);
                    for (float y = y0 + 1; y < y1; y += 2) for (float x = x0 + 1; x < x1; x += 2)
                    {
                        var p = new Vector2(x, y);
                        foreach (var o in _order) if (InRect(o.r, p)) { if (o.s == sp) pts.Add(p); break; }
                    }
                }
                if (pts.Count == 0) { Main.Log.Msg($"GK2 spot {sp.Label} is completely covered by other spots: left out"); sp.Hidden = true; continue; }
                var m = Vector2.zero; foreach (var p in pts) m += p; m /= pts.Count;
                // Prefer points with room around them (all neighbours 4 away also pick this spot).
                Vector2 best = pts[0]; float bd = float.MaxValue;
                foreach (var p in pts)
                {
                    float d = (p - m).sqrMagnitude;
                    bool roomy = true;
                    foreach (var off in new[] { new Vector2(4, 0), new Vector2(-4, 0), new Vector2(0, 4), new Vector2(0, -4) })
                    { var q = p + off; bool mine = false; foreach (var o in _order) if (InRect(o.r, q)) { mine = o.s == sp; break; } if (!mine) { roomy = false; break; } }
                    if (!roomy) d += 10000;
                    if (d < bd) { bd = d; best = p; }
                }
                sp.Aim = best;
            }
            Mark("aim points");
            if (marks.Count > 0) Main.Log.Msg("GK2 close-up slow parts: " + string.Join(", ", marks));
            _spots.RemoveAll(x => x.Hidden);
            _spots.Sort((a, b) => Math.Abs(a.Aim.x - b.Aim.x) > 20 ? a.Aim.x.CompareTo(b.Aim.x) : a.Aim.y.CompareTo(b.Aim.y));
            int n = 0; foreach (var s in _spots) if (s.Name == null) s.Name = "spot " + (++n);
            Main.Log.Msg($"GK2 examination {_pic}: {_spots.Count} spots: " + string.Join("; ", _spots.ConvertAll(s => $"{s.Label} '{s.Name}' aim {s.Aim.x:0},{s.Aim.y:0}")));
        }

        public static void Poll()
        {
            var c = Gk2Map.InGame ? Cursor() : null;
            bool shown = c != null && Shown(c);
            float now = Time.unscaledTime;
            if (!shown)
            {
                if (_active)
                {
                    if (_lostAt < 0) _lostAt = now;
                    else if (now - _lostAt > 0.8f) { _active = false; Main.Log.Msg("GK2 examination closed"); }
                }
                return;
            }
            _lostAt = -1;
            try
            {
                string pic = ""; try { pic = Gk2Map.Upcut.bg().getUpcutID().ToString(); } catch { }
                if (!_active || pic != _pic)
                {
                    bool fresh = !_active;
                    _active = true; Build();
                    if (_diag++ < 6) Main.Log.Msg($"GK2 cursor: pos={Gk2Map.V2(c.mPos)} req={c.m_ReqPos.x:0.#},{c.m_ReqPos.y:0.#} area={c.m_AreaX},{c.m_AreaY},{c.m_AreaW},{c.m_AreaH} start={Gk2Map.V2(c.mStartPos)} end={Gk2Map.V2(c.mEndPos)} scroll={Scroll()}");
                    Speech.Say(Menus.Join(fresh ? "Examining" : "New picture", _spots.Count == 1 ? "1 spot" : _spots.Count + " spots", Description()), false, false);
                }
                var pos = CurPos(c) + Scroll();
                int over = SpotAt(pos);
                if (over != _lastOver)
                {
                    if (over >= 0 && _arriveAt < 0) Earcons.Play(OnChecked(c) ? Earcons.SpotChecked : Earcons.Spot);
                    _lastOver = over;
                }
                if (_scrollTo.x >= 0)
                {
                    if ((Scroll() - _scrollTo).magnitude < 1f || now - _scrollSince > 2.5f)
                    {
                        // A move to the right that settled somewhere else shows how far this kind of picture scrolls.
                        var got = Scroll();
                        if (_scrollTo.x > 0 && got.x > 50 && Math.Abs(got.x - _wideMax) >= 1) { Main.Log.Msg($"GK2 wide pictures scroll {got.x}, not {_wideMax}"); _wideMax = got.x; }
                        _scrollTo = new Vector2(-1, -1);
                        Main.Log.Msg($"GK2 picture scroll now {got}, bgOffs={BgOffs()}");
                        if (_index >= 0 && _index < _spots.Count) Go(c, _spots[_index]);   // scrolls again if the spot needs the other half
                    }
                    return;
                }
                if (_arriveAt > 0 && now >= _arriveAt)
                {
                    // Still gliding there (e.g. after several quick presses): wait until it stops, up to 2 seconds.
                    var cp = CurPos(c);
                    if ((cp - _lastPos).magnitude > 0.5f && now - _stepAt < 2f) { _lastPos = cp; _arriveAt = now + 0.1f; return; }
                    _arriveAt = -1;
                    if (_index < 0 || _index >= _spots.Count) return;
                    var s = _spots[_index];
                    int at = SpotAt(CurPos(c) + Scroll());
                    Main.Log.Msg($"GK2 cursor arrived: pos={Gk2Map.V2(c.mPos)} want {s.Aim.x:0},{s.Aim.y:0} spot={at} checked={OnChecked(c)}");
                    if (at == _index && OnChecked(c)) Speech.Say("checked", false, false);
                    else if (at != _index) Speech.Say("The cursor didn't get there.", false, false);
                }
            }
            catch (Exception e) { if (_diag++ < 20) Main.Log.Warning("GK2 examination poll: " + e.Message); }
        }

        private static G2.cSubscrnTaskCheckMoveButton MoveButton()
        {
            try
            {
                var sc = G2.cSubscrnCtrl.Instance; if (sc == null) return null;
                var t = sc.search_Impl(G2.GKJ2Global.SUBSCRN_TASK_NAME.SUBSCRN_TASK_NAME_CHECK_MOVE_BUTTON);
                return t?.TryCast<G2.cSubscrnTaskCheckMoveButton>();
            }
            catch { return null; }
        }

        private static string BgOffs() { try { return Gk2Map.Upcut.mBgOffs.ToString(); } catch { return "?"; } }

        private static Vector2 _scrollTo = new Vector2(-1, -1);
        private static float _scrollSince;

        private static void MoveCursor(G2.cSubscrnTaskCheckCursor c, Spot s)
        {
            var aim = s.Aim - Scroll();
            c.RequestCursorPos(aim.x, aim.y);
            _arriveAt = Time.unscaledTime + 0.4f; _stepAt = Time.unscaledTime; _lastPos = new Vector2(-999, -999);
            Main.Log.Msg($"GK2 cursor to spot {s.Label} '{s.Name}' at {aim.x:0},{aim.y:0} (scroll {Scroll()}); was {Gk2Map.V2(c.mPos)}");
        }

        /// <summary>Which scroll position shows a picture point within the cursor's reach (tall pictures scroll 192 down, wide ones 256 across).</summary>
        // How far wide pictures scroll to the right (the picture is wider than the screen by this much); learnt from
        // where the game's own scroll stops.
        private static float _wideMax = 171;

        private static Vector2 WantScroll(Vector2 p, out G2.GKJ2Global.UPCUT_POS posId)
        {
            var cur = Scroll(); posId = G2.GKJ2Global.UPCUT_POS.UPCUT_POS_U;
            G2.GKJ2Global.UPCUT_TYPE type = G2.GKJ2Global.UPCUT_TYPE.UPCUT_TYPE_NORMAL;
            try { type = Gk2Map.Upcut.bg().getUpcutType(); } catch { }
            if (type == G2.GKJ2Global.UPCUT_TYPE.UPCUT_TYPE_LONG)
            {
                float y = p.y - cur.y;
                bool rest = Math.Abs(cur.y) < 1 || Math.Abs(cur.y - 192) < 1;   // stopped part way: always settle it
                if (rest && y >= 22 && y <= 176) return cur;
                bool down = rest ? p.y > 176 : p.y > 195;
                posId = down ? G2.GKJ2Global.UPCUT_POS.UPCUT_POS_D : G2.GKJ2Global.UPCUT_POS.UPCUT_POS_U;
                return new Vector2(cur.x, down ? 192 : 0);
            }
            if (type == G2.GKJ2Global.UPCUT_TYPE.UPCUT_TYPE_WIDE)
            {
                float x = p.x - cur.x;
                bool rest = Math.Abs(cur.x) < 1 || Math.Abs(cur.x - _wideMax) < 1;
                if (rest && x >= 6 && x <= 335) return cur;
                bool right = rest ? x > 335 : p.x > 6 + _wideMax + (329 - _wideMax) / 2;
                posId = right ? G2.GKJ2Global.UPCUT_POS.UPCUT_POS_R : G2.GKJ2Global.UPCUT_POS.UPCUT_POS_L;
                return new Vector2(right ? _wideMax : 0, cur.y);
            }
            return cur;
        }

        public static void Step(int dir)
        {
            try
            {
                var c = Cursor();
                if (c == null || _spots.Count == 0) { Speech.Say("No spots here.", true, false); return; }
                _index = _index < 0 ? (dir > 0 ? 0 : _spots.Count - 1) : (_index + dir + _spots.Count) % _spots.Count;
                var s = _spots[_index];
                Speech.Say(s.Name, true, false);
                // Still scrolling from an earlier press: let it finish, then go to this spot (a second scroll on top of
                // a moving one leaves the picture stuck part way).
                if (_scrollTo.x >= 0) return;
                Go(c, s);
            }
            catch (Exception e) { Main.Log.Error("GK2 examination step: " + e); }
        }

        private static int _scrolls;

        private static void Go(G2.cSubscrnTaskCheckCursor c, Spot s)
        {
            try
            {
                // Pictures bigger than the screen: scroll to the half that holds the spot first.
                var want = WantScroll(s.Aim, out var posId);
                var now0 = Scroll();
                bool atRest = (Math.Abs(now0.y) < 1 || Math.Abs(now0.y - 192) < 1) && (Math.Abs(now0.x) < 1 || Math.Abs(now0.x - _wideMax) < 1);
                if ((want - now0).magnitude > 1f && _scrolls < 3)
                {
                    _scrolls++;
                    var mb = atRest ? MoveButton() : null;   // stuck part way: just move it, the arrow would flip the wrong way
                    if (mb != null)
                    {
                        // The game's own "move the picture" arrow: it keeps the game's idea of what the cursor points at in step.
                        // Exactly what the game does when the arrow is pressed: the arrow's animation, then a move to the other half
                        // by position name (so the game also knows which half is showing), at the game's own speed.
                        try { mb.reqUpcutMove(); Gk2Map.Upcut.bg().reqMove(posId, 0x4000); } catch (Exception e) { Main.Log.Warning("GK2 move button: " + e.Message); }
                        _scrollTo = want; _scrollSince = Time.unscaledTime;
                        Main.Log.Msg($"GK2 picture move button pressed for spot {s.Label} (want {want.x:0},{want.y:0}); bgOffs={BgOffs()}");
                        return;
                    }
                    try
                    {
                        // Glide there over about a third of a second (the game's own move, to an exact position).
                        G2.cFx32 gx = new G2.cFx32(), gy = new G2.cFx32(); gx.mVal = (int)(want.x * 4096); gy.mVal = (int)(want.y * 4096);
                        var goal = new G2.cVector2D(); goal.x = gx; goal.y = gy;
                        Gk2Map.Upcut.bg().reqMove(posId, 0x4000);
                    }
                    catch (Exception e) { Main.Log.Warning("GK2 scroll: " + e.Message); }
                    _scrollTo = want; _scrollSince = Time.unscaledTime;
                    Main.Log.Msg($"GK2 scrolling picture to {posId} ({want.x:0},{want.y:0}) for spot {s.Label} from {now0}");
                    return;
                }
                _scrolls = 0;
                MoveCursor(c, s);
            }
            catch (Exception e) { Main.Log.Error("GK2 examination go: " + e); }
        }

        /// <summary>The whole-picture description (closeups.txt, keyed "cut00_001").</summary>
        private static string Description()
        {
            string k = _pic.ToLowerInvariant();
            if (k.StartsWith("upcut_id_")) k = "cut" + k.Substring(9);
            string d = Closeups.Whole(k);
            // Variants of a picture (the tutorial copy, download versions) share the original's description.
            for (string b = k; d.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(b, "_[a-z]+$"); )
            { b = System.Text.RegularExpressions.Regex.Replace(b, "_[a-z]+$", ""); d = Closeups.Whole(b); }
            if (d.Length == 0) Main.Log.Msg($"GK2 close-up {k}: no description");
            return d;
        }

        public static void ReadStatus()
        {
            try
            {
                var c = Cursor(); if (c == null) { Speech.Say("No cursor.", true, false); return; }
                var p = CurPos(c);
                int at = SpotAt(p + Scroll());
                string where = Compass(p);
                string on = at < 0 ? "not on a spot" : _spots[at].Name + (OnChecked(c) ? ", checked" : "");
                Speech.Say(Menus.Join(Description(), "Cursor " + where, on, _spots.Count + " spots"), true, false);
            }
            catch (Exception e) { Main.Log.Error("GK2 examination status: " + e.Message); }
        }

        private static string Compass(Vector2 p)
        {
            float fx = p.x / 341f, fy = p.y / 192f;
            string v = fy < 1 / 3f ? "top" : fy > 2 / 3f ? "bottom" : "", h = fx < 1 / 3f ? "left" : fx > 2 / 3f ? "right" : "";
            string d = v.Length > 0 && h.Length > 0 ? v + " " + h : v + h;
            return d.Length == 0 ? "in the middle" : "at the " + d;
        }
    }
}
