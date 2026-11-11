extern alias GK1;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>
    /// Close-up examination (first game): a picture with a cursor and hidden examinable spots.
    /// Like the Phoenix Wright mod: [ and ] jump the cursor between spots, a sound plays when the
    /// cursor is over a spot, and F3 says where the cursor is.
    /// </summary>
    internal static class ExamineScreen
    {
        private struct SpotInfo { public int X, Y, W, H; public uint Num; public int TX, TY; }

        private static bool _active;
        private static List<SpotInfo> _spots = new List<SpotInfo>();
        private static int _index = -1;
        private static bool _wasOnPoint;
        private static int _announceIn = -1;   // frames until we report the spot the cursor was moved to
        private static float _lastShown = -10f, _nextLog;
        private static bool _waitingArrive, _saidChecked;
        private static readonly HashSet<uint> _known = new HashSet<uint>();
        // Spots the cursor landed on without the game seeing a spot there (switched off for now), and
        // spots confirmed to be there.
        private static readonly HashSet<uint> _inactive = new HashSet<uint>(), _confirmed = new HashSet<uint>();
        private static int _lastDir = 1, _skips;
        private static string _sig = "";
        private static string _pic = "";   // the close-up picture's name, e.g. upcut_01_33
        private static readonly HashSet<string> _examined = new HashSet<string>();
        private static bool _spaceWas;
        private static string Key(SpotInfo s) => s.Num.ToString();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        private static int _arriveX, _arriveY, _arriveFrames, _prevX = int.MinValue, _prevY;

        private static bool CursorShown(GK1::Il2Cpp.GKJ1 g) => CursorWhich(g) != "";

        // Pictures taller than the screen scroll: the spots are in picture coordinates, the cursor in
        // screen coordinates, and the game keeps how far the picture is scrolled down in inq_py.
        private const int CurMinY = 28, CurMaxY = 170;
        private static int _bandLo, _bandHi = 10000, _startPy;
        // A picture taller than the screen: the cursor scrolls it when pushed against the top or bottom edge.
        private static bool _scrollable;
        private static int Height(GK1::Il2Cpp.GKJ1 g) { try { return g.GetInquiryData()?.inq_bg?.my ?? 0; } catch { return 0; } }
        private static readonly List<SpotInfo> _rects = new List<SpotInfo>();
        private static int _picW = 256, _picH = 192;
        private static int _scrollDir, _scrollEdgeY;
        private static string _scrollHow = "";
        private static float _scrollSince;
        private static string _bgSig = "";
        // How far the picture is scrolled down: the background's position (inq_py is not it: it is 192 even for
        // pictures that fit on the screen).
        // The screen shows 341 picture units across; wider pictures scroll sideways (inq_bg.dx).
        private const int ScreenW = 341, CurMinX = 8, CurMaxX = 333;
        private static int Sx(GK1::Il2Cpp.GKJ1 g) { try { var b = g.GetInquiryData()?.inq_bg; return b == null ? 0 : b.dx >> 16; } catch { return 0; } }

        /// <summary>Scroll a wide picture sideways so picture column x is on the screen; returns the screen x.</summary>
        private static int ShowColumn(GK1::Il2Cpp.GKJ1 g, int x)
        {
            int sx = Sx(g), cx = x - sx;
            if (cx >= CurMinX && cx <= CurMaxX) return cx;
            try
            {
                var b = g.GetInquiryData()?.inq_bg;
                if (b != null && b.mx > ScreenW)
                {
                    int want = Math.Max(0, Math.Min(b.mx - ScreenW, x - ScreenW / 2));
                    b.dx = want << 16;
                    Main.Log.Msg($"Scrolled sideways: dx {sx} -> {want} (picture {b.mx} wide)");
                    return x - want;
                }
            }
            catch (Exception e) { Main.Log.Msg("Sideways scroll: " + e.Message); }
            return Math.Max(CurMinX, Math.Min(CurMaxX, cx));
        }

        private static int Py(GK1::Il2Cpp.GKJ1 g) { try { var b = g.GetInquiryData()?.inq_bg; return b == null ? 0 : b.dy >> 16; } catch { return 0; } }
        private static string BgSig(GK1::Il2Cpp.GKJ1 g)
        {
            try { var d = g.GetInquiryData(); var b = d?.inq_bg; return $"py={d?.inq_py} bg dx={b?.dx} dy={b?.dy} mx={b?.mx} my={b?.my} move={b?.move} type={b?.type}"; } catch { return ""; }
        }

        /// <summary>Which inquiry cursor is showing ("" when none): the close-up one, the map one, or the flag.</summary>
        private static string CursorWhich(GK1::Il2Cpp.GKJ1 g)
        {
            try { var c = g.m_CursorObj; if (c != null && !c.WasCollected && c.m_IsDisplayed) return "closeup"; } catch { }
            try { var c = g.MapInquirySousaDetails?.CursorObj; if (c != null && !c.WasCollected && c.m_IsDisplayed) return "map"; } catch { }
            try { var c = g.m_PointCursorDecision?.CursorObj; if (c != null && !c.WasCollected && c.m_IsDisplayed) return "point"; } catch { }
            try { var c = g.m_SousaDetails?.CursorObj; if (c != null && !c.WasCollected && c.m_IsDisplayed) return "details"; } catch { }
            try { if (g.C_Cur_Flg) return "flag"; } catch { }
            return "";
        }

        public static bool Active => _active;
        /// <summary>Deduction: point out the contradiction in the scene (or present evidence).</summary>
        public static bool Deduction => _active && (_infer || _pointOut);
        // A question like "What is the important piece of evidence in this scene?": point at the answer.
        // The game keeps no record of what was examined there (read_flg 0).
        private static bool _pointOut;
        public static bool DeductionOutside => !_active && _inferOutside;
        private static bool _infer, _inferOutside, _deduce;
        private static float _outsideAnnounceAt = -1f;
        /// <summary>The game's "Deduce" button is up (a contradiction can be pointed out on this screen).</summary>
        private static bool DeduceShown(GK1::Il2Cpp.GKJ1 g)
        {
            if (_deduceHook) return true;
            try
            {
                float now = Time.unscaledTime;
                if (now >= _nextDeduceScan)
                {
                    _nextDeduceScan = now + 0.5f;
                    _deduceSeen = false;
                    foreach (var b in UnityEngine.Object.FindObjectsOfType<Il2CppGK_Common.UI.Sousa.GKSuiriButton>())
                        if (b != null && b.gameObject.activeInHierarchy && b.IsOpend && !b.Transparent) { _deduceSeen = true; break; }
                }
                return _deduceSeen;
            }
            catch { return false; }
        }

        private static bool _deduceHook, _deduceSeen;
        private static float _nextDeduceScan;

        /// <summary>Harmony hook: the game opened / closed its Deduce button.</summary>
        public static void OnDeduceButton(bool open)
        {
            Main.Log.Msg("Deduce button " + (open ? "opened" : "closed") + " by the game");
            _deduceHook = open;
        }

        private static string _stateSig = "";
        private static int FileCount(GK1::Il2Cpp.GKJ1 g) { try { return (int)(g.m_InquiryData?.m_InquiryCnt ?? 0); } catch { return 0; } }
        private static int LiveCount(GK1::Il2Cpp.GKJ1 g) { try { return (int)(g.GetInquiryData()?.inquiry_cnt ?? 0); } catch { return 0; } }
        private static string Mode(GK1::Il2Cpp.GKJ1 g) { try { var r = g.GetRecordData(); return r == null ? "?" : r.mode_main.ToString(); } catch { return "?"; } }
        private static int _hp = -1;

        private static bool InferMode(GK1::Il2Cpp.GKJ1 g) { try { return g.IsInferMode(); } catch { return false; } }
        /// <summary>The truth gauge (how many mistakes you can still make).</summary>
        public static int Hp(GK1::Il2Cpp.GKJ1 g) { try { return g.GetHpNow(); } catch { return -1; } }

        private static GK1::Il2Cpp.GKJ1 Game
        {
            get { try { return GK1::Il2Cpp.AppCore.gkj1; } catch { return null; } }
        }

        public static void Poll()
        {
            try
            {
                var g = Game;
                float now = Time.unscaledTime;
                bool cursor = g != null && CursorShown(g);
                bool shown = cursor && (FileCount(g) > 0 || LiveCount(g) > 0);
                // Log what the game looks like whenever something changes (to recognise new kinds of screens).
                if (g != null)
                {
                    string sig = $"cursor={CursorWhich(g)} deduce={DeduceShown(g)} file={FileCount(g)} live={LiveCount(g)} infer={InferMode(g)} walk={Investigation.PlayerCanWalk} mode={Mode(g)}";
                    if (sig != _stateSig) { _stateSig = sig; Main.Log.Msg("Screen state: " + sig); }
                }
                if (shown) _lastShown = now;
                // The cursor flickers while the screen sets up: only treat it as closed after a second without it.
                bool on = shown || (_active && now - _lastShown < 1f);
                if (on != _active)
                {
                    _active = on;
                    if (on) Start(g);
                    else { Main.Log.Msg("Examination ended"); _index = -1; _deduceHook = false; if (_scrollDir != 0) { _scrollDir = 0; Navigator.HoldKeys(0); } }
                }
                // Deduction that isn't on a close-up picture (logged so we can learn how it looks).
                if (!on && g != null)
                {
                    bool infOut = InferMode(g);
                    if (infOut != _inferOutside)
                    {
                        _inferOutside = infOut;
                        int cnt = -1; try { cnt = (int)(g.m_InquiryData?.m_InquiryCnt ?? 0); } catch { }
                        Main.Log.Msg($"Infer mode {(infOut ? "on" : "off")} outside close-up: inquiryCnt={cnt} cursorShown={CursorShown(g)} canWalk={Investigation.PlayerCanWalk} hp={Hp(g)}");
                        // Usually a close-up opens a moment later and announces itself: wait to see.
                        if (infOut) _outsideAnnounceAt = Time.unscaledTime + 0.7f;
                    }
                }
                if (_outsideAnnounceAt > 0 && (on || !_inferOutside)) _outsideAnnounceAt = -1f;
                if (_outsideAnnounceAt > 0 && Time.unscaledTime >= _outsideAnnounceAt) { _outsideAnnounceAt = -1f; Speech.Announce("Deduction. " + Help.Hint()); }
                if (!on || g == null) return;

                bool deduce = DeduceShown(g);
                if (deduce != _deduce)
                {
                    _deduce = deduce; Main.Log.Msg("Deduce button " + (deduce ? "shown" : "hidden"));
                }
                bool inf = InferMode(g);
                if (inf != _infer) { _infer = inf; Main.Log.Msg("Infer mode now " + inf); if (inf && !_deduce) Speech.Announce("Deduction. " + Help.Hint()); }
                int hp = Hp(g);
                if (hp != _hp)
                {
                    Main.Log.Msg($"Truth gauge {_hp} -> {hp}");
                    _hp = hp;
                }

                if (now >= _nextLog)
                {
                    _nextLog = now + 2f;
                    try { Main.Log.Msg($"Examine cursor: C_Cur={g.C_Cur_dx},{g.C_Cur_dy} target={g.C_Cur_tx},{g.C_Cur_ty} get={g.GetCursorX()},{g.GetCursorY()} onPoint={g.IsInquiryPoint}"); } catch { }
                }

                // Sound when the cursor moves onto / off a spot (also when the player moves it with W A S D).
                bool onPoint = g.IsInquiryPoint;
                if (onPoint && !_wasOnPoint && !_waitingArrive && _scrollDir == 0) Earcons.Play(CursorChecked(g) ? Earcons.SpotChecked : Earcons.Spot);
                _wasOnPoint = onPoint;

                // Space while sitting on the spot we jumped to: remember it as examined.
                bool space = KeyMap.Down(Il2CppGK_System.Controller.ButtonType.A) || Pad.ConfirmDown;   // the confirm key (Space) or controller confirm
                if (space && !_spaceWas && onPoint)
                {
                    // The spot under the cursor (the game takes the first area in its list that contains it).
                    int px = (g.C_Cur_dx >> 16) + Sx(g), pyy = (g.C_Cur_dy >> 16) + Py(g);
                    foreach (var r in _rects)
                        if (Inside(r, px, pyy)) { _examined.Add(r.Num.ToString()); _known.Add(r.Num); Main.Log.Msg($"Examined spot num={r.Num} at {px},{pyy}"); break; }
                }
                _spaceWas = space;

                string bgs = BgSig(g);
                if (bgs != _bgSig)
                {
                    _bgSig = bgs; Main.Log.Msg("Close-up view: " + bgs);
                    int pnow = Py(g);
                    bool real = false; try { real = g.GetInquiryData()?.inq_bg?.my > 0; } catch { }
                    if (!_scrollable && pnow != _startPy && real && LiveCount(g) > 0)
                    {
                        Build(g);
                        Speech.Say($"Picture moved. {_spots.Count} spots.", true, false);
                    }
                }
                if (_scrollDir != 0 && _index >= 0 && _index < _spots.Count)
                {
                    var sp = _spots[_index];
                    int sy = sp.TY - Py(g);
                    if (sy >= CurMinY && sy <= CurMaxY) { _scrollDir = 0; Navigator.HoldKeys(0); Main.Log.Msg($"Scrolled ({_scrollHow}): py={Py(g)}"); Aim(g, sp.TX - Sx(g), sy); }
                    else if (now - _scrollSince > 1.5f)
                    {
                        _scrollDir = 0; Navigator.HoldKeys(0);
                        Main.Log.Msg($"Scroll failed: py={Py(g)} cursor {g.C_Cur_dx >> 16},{g.C_Cur_dy >> 16}");
                        Speech.Say("Couldn't scroll to it.", false, false);
                    }
                    else
                    {
                        // No key presses (they cut NVDA off): ask the game's own scroll routine first, and if the
                        // picture doesn't move, set the scroll offset directly.
                        float t = now - _scrollSince;
                        int before = Py(g);
                        try { g.SetCursorTargetPos(sp.TX - Sx(g), _scrollDir < 0 ? CurMinY : CurMaxY); } catch { }
                        if (t < 0.4f)
                        {
                            int btn = t < 0.2f ? (_scrollDir < 0 ? 64 : 128) : (_scrollDir < 0 ? 37 : 38);
                            try { for (int k = 0; k < 4; k++) g.Inquiry_MoveScroll(btn); } catch (Exception e) { Main.Log.Msg("MoveScroll: " + e.Message); _scrollSince -= 1f; }
                            if (Py(g) != before) _scrollHow = "game " + btn;
                        }
                        else
                        {
                            var b = g.GetInquiryData()?.inq_bg;
                            if (b != null)
                            {
                                int max = Math.Max(0, b.my - 192);
                                int want = Math.Max(0, Math.Min(max, sp.TY - 96));
                                b.dy = want << 16;
                                _scrollHow = "direct";
                            }
                        }
                    }
                }
                else if (_waitingArrive)
                {
                    _arriveFrames++;
                    int x = g.C_Cur_dx >> 16, y = g.C_Cur_dy >> 16;
                    bool there = x == _arriveX && y == _arriveY;
                    // The cursor can't reach every point (e.g. the top edge), so also accept "stopped moving".
                    bool still = _arriveFrames > 2 && x == _prevX && y == _prevY;
                    _prevX = x; _prevY = y;
                    if (there || still || _arriveFrames > 45) { _waitingArrive = false; _announceIn = 1; }   // then let the check mark update
                }
                else if (_announceIn > 0 && --_announceIn == 0) AnnounceCurrent(g);
            }
            catch (Exception e) { Main.Log.Error("Examine poll failed: " + e.Message); _active = false; }
        }

        private static void Start(GK1::Il2Cpp.GKJ1 g)
        {
            Build(g);
            _infer = InferMode(g);
            _pointOut = false;
            try { var live = g.GetInquiryData(); _pointOut = !_infer && live != null && live.inquiry_cnt > 0 && live.read_flg == 0; } catch { }
            _hp = Hp(g);
            Main.Log.Msg($"Close-up mode: infer={_infer} pointOut={_pointOut} hp={_hp}");
            // Pointing something out uses the deduce key (T / Triangle), not confirm.
            if (_pointOut) { Speech.Announce($"Deduction. {_spots.Count} spots. " + Pad.F("Point at the answer and press {0}.", Help.DeduceButton()) + " " + Help.Hint()); return; }
            Speech.Announce(_infer
                ? $"Deduction. {_spots.Count} spots. " + Pad.F("Point at what doesn't fit and press {0}.", Help.DeduceButton()) + " " + Help.Hint()
                : $"Examination. {_spots.Count} spots. " + Help.Hint());
        }

        private static void Build(GK1::Il2Cpp.GKJ1 g)
        {
            _spots.Clear();
            try
            {
                var bg = g.GetInquiryData()?.inq_bg;
                _pic = bg == null ? "" : PageExport.NcgName(bg.ncg) ?? "";
                Main.Log.Msg($"Close-up picture: {_pic} (ncg {bg?.ncg}) {(Closeups.Whole(_pic).Length > 0 ? "described" : "no description")}");
            }
            catch { _pic = ""; }
            // Only the part of the picture the cursor can reach right now (tall pictures show one half).
            _startPy = Py(g); _bandLo = _startPy + CurMinY; _bandHi = _startPy + CurMaxY;
            int h = Height(g);
            // Tall pictures can't actually be scrolled (holding the key at the edge does nothing): only the part on
            // screen counts.
            _scrollable = h > 200;
            if (_scrollable) { _bandLo = CurMinY; _bandHi = h - (192 - CurMaxY); }   // every row can be scrolled to
            // The live list (what the game actually tests the cursor against); fall back to the file data.
            var rects = new List<SpotInfo>();
            uint readFlg = 0;
            try
            {
                var live = g.GetInquiryData();
                if (live != null && live.inquiry_pos != null && live.inquiry_cnt > 0)
                {
                    readFlg = live.read_flg;
                    Main.Log.Msg($"Live inquiry data: cnt={live.inquiry_cnt} py={live.inq_py} read_flg={live.read_flg:X}");
                    int ln = (int)Math.Min(live.inquiry_cnt, (uint)live.inquiry_pos.Length);
                    for (int i = 0; i < ln; i++) AddRect(rects, live.inquiry_pos[i]);
                }
            }
            catch (Exception e) { Main.Log.Warning("Live inquiry data unavailable: " + e.Message); }
            if (rects.Count == 0)
            {
                var d = g.m_InquiryData;
                int n = (int)Math.Min(d.m_InquiryCnt, (uint)(d.m_InquiryPos?.Length ?? 0));
                for (int i = 0; i < n; i++) AddRect(rects, d.m_InquiryPos[i]);
            }
            // Areas with the same number are the same thing: one entry each, aimed at the rectangle
            // where a point is free of other things' areas.
            // Two numbers with exactly the same areas are the same spot to the player (the game uses the first one).
            var shapes = new Dictionary<uint, string>();
            foreach (var r in rects) shapes[r.Num] = (shapes.TryGetValue(r.Num, out var sh) ? sh + ";" : "") + $"{r.X},{r.Y},{r.W},{r.H}";
            var seenShape = new Dictionary<string, uint>();
            var dup = new HashSet<uint>();
            foreach (var r in rects)
            {
                string sh = shapes[r.Num];
                if (seenShape.TryGetValue(sh, out uint first)) { if (first != r.Num) dup.Add(r.Num); }
                else seenShape[sh] = r.Num;
            }
            if (dup.Count > 0)
            {
                foreach (var dn in dup) Main.Log.Msg($"Same areas as another spot, merged: {dn} = {seenShape[shapes[dn]]} ({shapes[dn]})");
                rects.RemoveAll(r => dup.Contains(r.Num));
            }
            var byNum = new Dictionary<uint, SpotInfo>();
            var bestScore = new Dictionary<uint, long>();
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                long score = FindOwnPoint(ref r, rects);
                if (score == long.MaxValue) continue;   // not in the part of the picture on screen
                if (!bestScore.TryGetValue(r.Num, out long b) || score < b) { bestScore[r.Num] = score; byNum[r.Num] = r; }
            }
            _spots.AddRange(byNum.Values);
            _rects.Clear(); _rects.AddRange(rects);
            var hidden = new HashSet<uint>(); foreach (var r in rects) if (!byNum.ContainsKey(r.Num)) hidden.Add(r.Num);
            Main.Log.Msg($"Reachable picture rows {_bandLo}-{_bandHi}; off screen: {string.Join(",", hidden)}");
            _picW = 256; _picH = 192; _scrollDir = 0;
            foreach (var r in rects) { _picW = Math.Max(_picW, r.X + r.W); _picH = Math.Max(_picH, r.Y + r.H); }
            // Same close-up as last time: keep what we learned about checked spots.
            string sig = string.Join(";", rects.ConvertAll(r => $"{r.Num}:{r.X},{r.Y},{r.W},{r.H}"));
            if (sig != _sig) { _sig = sig; _known.Clear(); _examined.Clear(); _inactive.Clear(); _confirmed.Clear(); }
            // Reading order: top to bottom, then left to right (by the point we aim at).
            _spots.Sort((a, b) => a.TY / 24 != b.TY / 24 ? a.TY.CompareTo(b.TY) : a.TX.CompareTo(b.TX));
            _index = -1;
            Main.Log.Msg($"Examination started: cursor {g.C_Cur_dx >> 16},{g.C_Cur_dy >> 16}; {rects.Count} areas, {_spots.Count} things");
            foreach (var r in rects) Main.Log.Msg($"  area num={r.Num} rect={r.X},{r.Y} {r.W}x{r.H}");
            foreach (var r in _spots) Main.Log.Msg($"  thing num={r.Num} aim={r.TX},{r.TY}");
        }

        private static void AddRect(List<SpotInfo> list, GK1::Il2Cpp.INQUIRY_POS p)
        {
            if (p == null || p.ex <= p.sx || p.ey <= p.sy) return;
            if (list.Exists(o => o.Num == p.num && o.X == p.sx && o.Y == p.sy && o.W == p.ex - p.sx && o.H == p.ey - p.sy)) return;
            list.Add(new SpotInfo { X = p.sx, Y = p.sy, W = p.ex - p.sx, H = p.ey - p.sy, Num = p.num });
            Main.Log.Msg($"  area num={p.num} key={p.key:X}");
        }

        private static bool Inside(SpotInfo o, int x, int y) => x >= o.X && x < o.X + o.W && y >= o.Y && y < o.Y + o.H;

        /// <summary>Picks a point in the rectangle that no other thing's area covers; returns its score (lower is better).</summary>
        private static long FindOwnPoint(ref SpotInfo s, List<SpotInfo> all)
        {
            int lo = Math.Max(s.Y, _bandLo), hi = Math.Min(s.Y + s.H, _bandHi + 1);
            int cx = s.X + s.W / 2, cy = (lo + hi) / 2;
            int bestX = cx, bestY = cy;
            long best = long.MaxValue;
            for (int y = s.Y + 1; y < s.Y + s.H - 1; y += 2)
                if (y >= _bandLo && y <= _bandHi)
                for (int x = s.X + 1; x < s.X + s.W - 1; x += 2)
                {
                    int covered = 0;
                    foreach (var o in all) if (o.Num != s.Num && Inside(o, x, y)) covered++;
                    int edge = Math.Min(Math.Min(x - s.X, s.X + s.W - 1 - x), Math.Min(y - s.Y, s.Y + s.H - 1 - y));
                    long score = covered * 1000000L + (edge < 5 ? 50000 : 0) + (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (score < best) { best = score; bestX = x; bestY = y; }
                }
            s.TX = bestX; s.TY = bestY;
            return best;
        }

        /// <summary>[ and ]: move the cursor to the previous / next spot.</summary>
        public static void Step(int dir) { _skips = 0; StepInner(dir); }

        private static int ActiveCount() { int n = 0; foreach (var s in _spots) if (!_inactive.Contains(s.Num)) n++; return n; }
        private static int ActivePos(int index) { int n = 0; for (int i = 0; i <= index && i < _spots.Count; i++) if (!_inactive.Contains(_spots[i].Num)) n++; return n; }

        private static void StepInner(int dir)
        {
            var g = Game;
            if (!_active || g == null) return;
            if (_spots.Count == 0 || ActiveCount() == 0) { Speech.Say("No spots here.", true, false); return; }
            _lastDir = dir;
            do { _index = (_index + dir + _spots.Count) % _spots.Count; } while (_inactive.Contains(_spots[_index].Num));
            var s = _spots[_index];
            int py = Py(g), cx = ShowColumn(g, s.TX), cy = s.TY - py;
            Main.Log.Msg($"Cursor to spot {_index + 1} (picture {s.TX},{s.TY}, screen {cx},{cy}, py {py}); now C_Cur={g.C_Cur_dx >> 16},{g.C_Cur_dy >> 16}");
            // Said straight away; once the cursor lands, "checked" / "not checked" is corrected if needed.
            _saidChecked = _known.Contains(s.Num) || _examined.Contains(Key(s));
            _spokeEarly = true;
            string what = SpotText(s.Num);
            if (what.Length == 0) what = Closeups.Spot(_pic, s.TX, s.TY, s.X, s.Y, s.W, s.H);
            Speech.Say(Menus.Join($"Spot {ActivePos(_index)} of {ActiveCount()}", what, Describe(s.TX, s.TY), _saidChecked ? "checked" : ""), true, false);
            if (_scrollable && (cy < CurMinY || cy > CurMaxY))
            {
                _scrollDir = cy < CurMinY ? -1 : 1; _scrollEdgeY = _scrollDir < 0 ? 0 : 191; _scrollSince = Time.unscaledTime;
                _waitingArrive = false; _announceIn = -1;
                Main.Log.Msg($"Spot is off screen; scrolling {(_scrollDir < 0 ? "up" : "down")}");
                return;
            }
            _scrollDir = 0; Navigator.HoldKeys(0);
            Aim(g, cx, Math.Max(CurMinY, Math.Min(CurMaxY, cy)));
        }

        private static void Aim(GK1::Il2Cpp.GKJ1 g, int cx, int cy)
        {
            try { g.SetCursorTargetPos(cx, cy); } catch (Exception e) { Main.Log.Warning("SetCursorTargetPos failed: " + e.Message); }
            _arriveX = cx; _arriveY = cy; _arriveFrames = 0; _prevX = int.MinValue; _announceIn = -1; _waitingArrive = true;
        }

        private static bool _spokeEarly;

        private static void AnnounceCurrent(GK1::Il2Cpp.GKJ1 g)
        {
            if (_index < 0 || _index >= _spots.Count) return;
            var s = _spots[_index];
            bool onPoint = g.IsInquiryPoint;
            bool done = CursorChecked(g) || _examined.Contains(Key(s));
            Main.Log.Msg($"Arrived: num={s.Num} cursor={g.C_Cur_dx >> 16},{g.C_Cur_dy >> 16} onPoint={onPoint} mark={CursorChecked(g)} said={_saidChecked}");
            _confirmed.Add(s.Num);
            if (done) _known.Add(s.Num);
            if (done && !_saidChecked) Speech.Say("checked", false, false);
            else if (!done && _saidChecked) Speech.Say("not checked", false, false);
        }

        /// <summary>The description of a spot from all of its boxes together.</summary>
        private static string SpotText(uint num)
        {
            var rs = new List<(int x, int y, int w, int h)>();
            foreach (var r in _rects) if (r.Num == num) rs.Add((r.X, r.Y, r.W, r.H));
            return rs.Count == 0 ? "" : Closeups.SpotArea(_pic, rs);
        }

        /// <summary>F3: where is the cursor, and is it on a spot?</summary>
        public static void ReadStatus()
        {
            var g = Game;
            if (!_active || g == null) return;
            string where = Describe((g.C_Cur_dx >> 16) + Sx(g), (g.C_Cur_dy >> 16) + Py(g));
            string on = g.IsInquiryPoint ? (CursorChecked(g) ? "on a checked spot" : "on a spot") : "not on a spot";
            string what = "";
            if (g.IsInquiryPoint)
            {
                int cx = (g.C_Cur_dx >> 16) + Sx(g), cy = (g.C_Cur_dy >> 16) + Py(g);
                foreach (var r in _rects) if (cx >= r.X && cx < r.X + r.W && cy >= r.Y && cy < r.Y + r.H) { what = SpotText(r.Num); if (what.Length == 0) what = Closeups.Spot(_pic, cx, cy, r.X, r.Y, r.W, r.H); break; }
            }
            string pic = Closeups.Whole(_pic);
            Speech.Say((pic.Length > 0 ? pic + " " : "") + ($"{(_infer || _pointOut ? "Deduction. " : "")}Cursor {where}, {on}{(what.Length > 0 ? ": " + what : "")}. {_spots.Count} spots."), true, false);
        }

        private static bool CursorChecked(GK1::Il2Cpp.GKJ1 g)
        {
            try
            {
                var c = g.m_CursorObj;
                return c != null && c.m_CheckMarkObj != null && c.m_CheckMarkObj.activeInHierarchy;
            }
            catch { return false; }
        }

        /// <summary>Rough position on screen (given picture coordinates): "top left", "center", ...</summary>
        private static string Describe(int x, int y)
        {
            string h = x < _picW / 3 ? "left" : x > _picW * 2 / 3 ? "right" : "center";
            if (!_scrollable) y -= _startPy;   // on the screen
            else y = y * 192 / Math.Max(192, _picH);   // on the whole tall picture
            string v = y < 64 ? "top" : y > 128 ? "bottom" : "middle";
            if (v == "middle") return h == "center" ? "center" : "middle " + h;
            return h == "center" ? v + " center" : v + " " + h;
        }
    }
}
