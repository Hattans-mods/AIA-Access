extern alias GK1;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>
    /// 3D evidence examination (first game): turn a piece of evidence around and look for details.
    /// Like the Phoenix Wright mod: [ and ] go to the next / previous detail. The game's own table of
    /// details says at which angle each one can be seen; the mod turns the evidence to that angle
    /// (with the game's own "auto select" turn) and then finds the cursor position that hits it.
    /// </summary>
    internal static class Examine3D
    {
        private static readonly HashSet<int> _unfound = new HashSet<int>();
        // One detail of the evidence. The game's table has several faces per detail (and "blocker" faces with
        // ret -1); a detail is one ret value, with the angle(s) the game turns the evidence to for it.
        private class Spot { public int Ret; public bool AnimOnly = true; public List<Vector3> Rots = new List<Vector3>(); public List<float> Scales = new List<float>(); }
        // Where the cursor can go on the 3D screen, and how much further away to look when a detail is at the edge.
        private const int CurMinX = 10, CurMaxX = 246, CurMinY = 10, CurMaxY = 182;
        private static readonly float[] ZoomOut = { 0.75f, 0.55f };
        private static int _tryRot, _zoomTry;

        // The game's own autoplay data (Assets/GK_System/Tuning/AutoPlay/EA/gk1_sN_detail3d): for the details the
        // story needs, the angle, zoom and cursor position its test player uses. id is the detail's ret value.
        private struct Ap
        {
            public int Ep, Id, X, Y; public float Scale; public Vector3 Rot;
            public Ap(int ep, int id, int x, int y, float sc, float rx, float ry, float rz) { Ep = ep; Id = id; X = x; Y = y; Scale = sc; Rot = new Vector3(rx, ry, rz); }
        }
        private static readonly Ap[] AutoPlay =
        {
            new Ap(4, 1, 200, 70, 1.0f, 0.8404541015625f, 166.31103515625f, 143.887939453125f),   // ヤタガラスのカギ
            new Ap(4, 2, 197, 81, 1.0f, 355.8636474609375f, 153.819580078125f, 101.162109375f),   // ヤタガラスのカギ
            new Ap(4, 1, 176, 75, 1.0f, 320.0f, 257.0f, 270.0f),   // ババルナイフのツカ
            new Ap(4, 2, 194, 45, 3.0f, 309.0f, 246.0f, 293.0f),   // 模様
            new Ap(4, 1, 170, 75, 1.0f, 270.340576171875f, 301.0089111328125f, 58.623046875f),   // 手形
            new Ap(4, 1, 170, 45, 1.0f, 0.0f, 0.0f, 0.0f),   // 像の頭
            new Ap(4, 5, 218, 51, 2.9375f, 10.0f, 37.0f, 359.0f),   // 板
            new Ap(4, 1, 143, 51, 1.0f, 78.59619140625f, 100.1953125f, 289.8248291015625f),   // 血痕
            new Ap(4, 1, 164, 87, 1.989990234375f, 23.18115234375f, 86.7535400390625f, 81.3702392578125f),   // 手押し車の血痕
            new Ap(4, 1, 204, 75, 1.0f, 15.0f, 282.0f, 296.0f),   // 日の丸
            new Ap(1, 2, 185, 78, 1.0f, 24.0f, 52.0f, 37.0f),   // サイフ
            new Ap(1, 2, 188, 87, 3.0f, 2.3345947265625f, 75.9979248046875f, 9.8162841796875f),   // スーツケースのレバー
            new Ap(1, 4, 164, 57, 1.0f, 83.397216796875f, 296.9879150390625f, 125.83740234375f),   // 裏面のカメラのレンズ
            new Ap(3, 1, 146, 75, 1.0f, 4.0814208984375f, 12.2991943359375f, 232.40478515625f),   // ヤタガラスのカギ
        };
        private static List<Ap> _apCands = new List<Ap>();
        private static int _apTry = -1;

        private static bool _active, _sessionOver;
        private static IntPtr _model = IntPtr.Zero;
        private static float _lastSeen = -10f;
        private static readonly List<Spot> _spots = new List<Spot>();
        private static int _index = -1;
        private static int _lastHit = int.MinValue;

        // Moving to a spot: wait for the turn to finish, then search for the cursor position.
        private static int _pending = -1;
        private static float _pendingSince;

        public static bool Active => _active;

        private static GK1::Il2Cpp.GKJ1 Game { get { try { return GK1::Il2Cpp.AppCore.gkj1; } catch { return null; } } }

        private static GK1::Il2Cpp.ItemListMenu.ITEM3D Item(out GK1::Il2Cpp.ItemListMenu menu)
        {
            menu = null;
            try
            {
                menu = GK1::Il2Cpp.GKJ1.ItemList;
                var it = menu?.item3D;
                if (it == null || it.ModelWork == null) return null;
                return it;
            }
            catch { return null; }
        }

        public static void Poll()
        {
            try
            {
                var g = Game;
                GK1::Il2Cpp.ItemListMenu menu = null;
                var it = g == null ? null : Item(out menu);
                float now = Time.unscaledTime;
                // A newly loaded model starts a session; walking again (or the model going away) ends it.
                IntPtr model = IntPtr.Zero;
                try { if (it != null) model = it.ModelWork.Pointer; } catch { }
                if (model != _model)
                {
                    try { Main.Log.Msg($"3D model {_model} -> {model}; mode={menu?.itemListMode} check3d={menu?.check_item_3d} disp={menu?.isDispCursor} kind={it?.kind} hiCount={it?.hiCount}"); } catch { }
                    _model = model; _sessionOver = false;
                }
                if (_active && Investigation.PlayerCanWalk) _sessionOver = true;
                bool shown = it != null && model != IntPtr.Zero && !_sessionOver && !ExamineScreen.Active;
                if (shown) _lastSeen = now;
                bool on = shown || (_active && now - _lastSeen < 1f);
                if (on != _active)
                {
                    _active = on;
                    if (on) Start(g, it);
                    else { Main.Log.Msg("3D examination ended"); _spots.Clear(); _index = -1; _pending = -1; }
                }
                if (!_active || it == null) return;

                // Sound when the cursor is on a detail (the game's own check).
                int hit = it.hitIdx;
                if (hit != _lastHit)
                {
                    Main.Log.Msg($"3D hitIdx {_lastHit} -> {hit}; cursor {g.C_Cur_dx >> 16},{g.C_Cur_dy >> 16} rot={it.totalRot}");
                    if (_lastHit != int.MinValue && hit >= 0 && _pending < 0) Earcons.Play(Earcons.Spot);
                    _lastHit = hit;
                }

                if (_pending >= 0)
                {
                    bool turning = false;
                    try { turning = it.autoSelectFlg; } catch { }
                    if (!turning || now - _pendingSince > 3f) FinishMove(g, it);
                }
            }
            catch (Exception e) { Main.Log.Error("3D poll failed: " + e.Message); _active = false; }
        }

        private static void Start(GK1::Il2Cpp.GKJ1 g, GK1::Il2Cpp.ItemListMenu.ITEM3D it)
        {
            _spots.Clear(); _index = -1; _pending = -1; _lastHit = int.MinValue; _unfound.Clear();
            try
            {
                float sc = GK1::Il2Cpp.GKJ1.ItemList.GetIteamScale();
                Main.Log.Msg($"3D examination started: kind={it.kind} hitIdx={it.hitIdx} hiCount={it.hiCount} rot={it.rot} totalRot={it.totalRot} scale={sc}");
                var table = HitTable(it);
                if (table != null)
                {
                    for (int i = 0; i < table.Length; i++)
                    {
                        var h = table[i];
                        if (h == null) continue;
                        var vt = h.vecTbl;
                        var c = Vector2.zero;
                        int n = vt == null ? 0 : vt.Length;
                        for (int k = 0; k < n; k++) c += new Vector2(vt[k].x, vt[k].y);
                        if (n > 0) c /= n;
                        if (h.ret >= 0)
                        {
                            var s = _spots.Find(x => x.Ret == h.ret);
                            if (s == null) { s = new Spot { Ret = h.ret }; _spots.Add(s); }
                            if (!h.anime) s.AnimOnly = false;   // at least one of its faces is there without any animation
                            var rot = new Vector3(h.angleX, h.angleY, h.angleZ);
                            if (!s.Rots.Contains(rot)) { s.Rots.Add(rot); s.Scales.Add(h.scale.Raw / 4096f); }
                        }
                        var pts = new List<string>();
                        for (int k = 0; k < n; k++) pts.Add($"{vt[k].x:0},{vt[k].y:0},{vt[k].z:0}");
                        Main.Log.Msg($"  detail {i}: ret={h.ret} anime={h.anime} angle={h.angleX},{h.angleY},{h.angleZ} scale={h.scale.Raw} trans={h.trans} count={h.count} poly=[{string.Join(" ", pts)}]");
                    }
                }
            }
            catch (Exception e) { Main.Log.Error("3D table read failed: " + e); }
            Speech.Announce($"3D examination. {(_spots.Count == 1 ? "1 detail. " : _spots.Count > 1 ? _spots.Count + " details. " : "")}" + Help.Hint());
        }

        /// <summary>The game's table of details for the evidence being shown.</summary>
        private static GK1::Il2Cpp.ItemListMenu.ITEM3D_HITINFO[] HitTable(GK1::Il2Cpp.ItemListMenu.ITEM3D it)
        {
            var ro = GK1::Il2Cpp.ItemListMenu.ReadOnly;
            var infos = ro?.ItemHitInfo;
            if (infos == null) { Main.Log.Msg("3D: no hit table"); return null; }
            Main.Log.Msg($"3D: {infos.Length} hit tables; using kind {it.kind}");
            if (it.kind < 0 || it.kind >= infos.Length || infos[it.kind] == null) return null;
            return infos[it.kind].HitInfoTbl;
        }

        /// <summary>[ and ]: turn the evidence to the next / previous detail and put the cursor on it.</summary>
        public static void Step(int dir)
        {
            var g = Game;
            var it = Item(out var menu);
            if (!_active || g == null || it == null) return;
            if (_spots.Count == 0) { Speech.Say("No details found.", true, false); return; }
            // Details that couldn't be found are skipped from then on.
            if (_unfound.Count >= _spots.Count) { Speech.Say("None of the details can be found right now.", true, false); return; }
            do _index = (_index + dir + _spots.Count) % _spots.Count; while (_unfound.Contains(_index));
            _tryRot = 0; _zoomTry = 0; _apTry = -1;
            TurnTo(it, menu);
        }

        private static void TurnTo(GK1::Il2Cpp.ItemListMenu.ITEM3D it, GK1::Il2Cpp.ItemListMenu menu)
        {
            var s = _spots[_index];
            try
            {
                var rot = s.Rots[_tryRot];
                float scale = s.Scales[_tryRot] > 0.05f ? s.Scales[_tryRot] : menu.GetIteamScale();
                Main.Log.Msg($"3D go to detail {_index} (ret {s.Ret}), angle {_tryRot + 1} of {s.Rots.Count}: rot {rot} scale {scale}");
                Turn(it, rot, scale);
            }
            catch (Exception e) { Main.Log.Warning("AutoSelectStart failed: " + e.Message); }
            _pending = _index; _pendingSince = Time.unscaledTime;
        }

        private static void FinishMove(GK1::Il2Cpp.GKJ1 g, GK1::Il2Cpp.ItemListMenu.ITEM3D it)
        {
            int i = _pending; _pending = -1;
            if (i < 0 || i >= _spots.Count) return;
            var s = _spots[i];
            // Look over the whole screen for where the cursor hits this detail (the game's own hit check).
            var hits = new List<Vector2>();
            var seen = new Dictionary<int, int>();
            // Turned the way the game's autoplay does it: check its cursor spot (and just around it) first.
            if (_apTry >= 0 && _apTry < _apCands.Count)
            {
                var a = _apCands[_apTry];
                for (int dy = -4; dy <= 4 && hits.Count == 0; dy += 2)
                    for (int dx = -4; dx <= 4 && hits.Count == 0; dx += 2)
                        if (it.IsHit(a.X + dx, a.Y + dy) == s.Ret) hits.Add(new Vector2(a.X + dx, a.Y + dy));
                Main.Log.Msg($"3D autoplay angle {_apTry + 1} of {_apCands.Count} for ret {s.Ret}: {(hits.Count > 0 ? "hit" : "no hit")} at {a.X},{a.Y}");
            }
            if (hits.Count == 0)
                for (int y = 1; y < 192; y += 2)
                    for (int x = 1; x < 256; x += 2)
                    {
                        int r = it.IsHit(x, y);
                        if (r < 0) continue;
                        if (!seen.ContainsKey(r)) seen[r] = 0;
                        seen[r]++;
                        if (r == s.Ret) hits.Add(new Vector2(x, y));
                    }
            var hist = new List<string>(); foreach (var kv in seen) hist.Add($"ret {kv.Key}: {kv.Value}");
            Main.Log.Msg($"3D detail {i} (ret {s.Ret}): {hits.Count} hit points; seen {string.Join(", ", hist)}");
            // The cursor can't go right to the screen's edges: only count the part it can reach.
            int all = hits.Count;
            hits.RemoveAll(p => p.x < CurMinX || p.x > CurMaxX || p.y < CurMinY || p.y > CurMaxY);
            if (hits.Count < all) Main.Log.Msg($"3D detail {i}: {hits.Count} of {all} hit points are where the cursor can go");
            if (hits.Count == 0 && all > 0 && _zoomTry < ZoomOut.Length)
            {
                // Seen, but only at the edge of the screen: turn it the same way, a little further away.
                var itm = Item(out var menu);
                if (itm != null)
                {
                    var rot = s.Rots[Math.Min(_tryRot, s.Rots.Count - 1)];
                    float baseScale = s.Scales[Math.Min(_tryRot, s.Scales.Count - 1)];
                    if (baseScale <= 0.05f) baseScale = menu.GetIteamScale();
                    float sc = baseScale * ZoomOut[_zoomTry++];
                    Main.Log.Msg($"3D: detail {i} only at the screen edge; trying further away, scale {sc}");
                    _index = i;
                    try { Turn(itm, rot, sc); } catch (Exception e) { Main.Log.Warning("AutoSelectStart failed: " + e.Message); }
                    _pending = i; _pendingSince = Time.unscaledTime;
                    return;
                }
            }
            if (hits.Count == 0)
            {
                // Not visible from this angle: try the detail's other angles, then closer up.
                if (++_tryRot < s.Rots.Count) { var itm = Item(out var menu); if (itm != null) { _index = i; TurnTo(itm, menu); return; } }
                // Then the angles the game's own autoplay uses for a detail with this number.
                if (_apTry < 0) { _apCands.Clear(); foreach (var a in AutoPlay) if (a.Id == s.Ret) _apCands.Add(a); }
                if (++_apTry < _apCands.Count)
                {
                    var itm = Item(out var menu);
                    if (itm != null)
                    {
                        var a = _apCands[_apTry]; _index = i;
                        Main.Log.Msg($"3D: detail {i} not seen; trying the autoplay angle {a.Rot} scale {a.Scale} (episode {a.Ep})");
                        try { Turn(itm, a.Rot, a.Scale); } catch (Exception e) { Main.Log.Warning("AutoSelectStart failed: " + e.Message); }
                        _pending = i; _pendingSince = Time.unscaledTime;
                        return;
                    }
                }
                bool onlyAnimated = s.AnimOnly;
                // Something on the evidence moving can bring an animated detail into view: only skip the others.
                if (!onlyAnimated) _unfound.Add(i);
                Speech.Say(onlyAnimated
                    ? $"Detail {i + 1} of {_spots.Count}: it only shows after something on the evidence moves. Examine the other details first."
                    : $"Detail {i + 1} of {_spots.Count}, couldn't find it. Skipping it from now on.", true, false);
                return;
            }
            // Aim at the middle of the visible part, away from its edges.
            var c = Vector2.zero; foreach (var p in hits) c += p; c /= hits.Count;
            var best = hits[0]; float bd = float.MaxValue;
            foreach (var p in hits) { float d = (p - c).sqrMagnitude; if (d < bd) { bd = d; best = p; } }
            // Straight there: the cursor's own position as well as where it is heading, so it doesn't glide.
            try { g.SetCursorTargetPos((int)best.x, (int)best.y); g.C_Cur_dx = (int)best.x << 16; g.C_Cur_dy = (int)best.y << 16; } catch (Exception e) { Main.Log.Warning("3D cursor move failed: " + e.Message); }
            Main.Log.Msg($"3D cursor to {best} for ret {s.Ret}");
            // Only now, with the cursor really on it.
            Speech.Say($"Detail {i + 1} of {_spots.Count}", true, false);
        }

        /// <summary>Turns the evidence the way the game does for a detail, but at once: the game's turn takes 30 frames
        /// (autoRotFrame counts them) and ends exactly on the angle and size asked for, so start it at the last frame.</summary>
        private static void Turn(GK1::Il2Cpp.ItemListMenu.ITEM3D it, Vector3 rot, float scale)
        {
            it.AutoSelectStart(rot, scale);
            try { it.autoRotFrame = 29; } catch { }
        }

        private static bool HitAt(GK1::Il2Cpp.ItemListMenu.ITEM3D it, int x, int y, int ret)
        {
            try { int r = it.IsHit(x, y); Main.Log.Msg($"3D IsHit({x},{y}) = {r} (want {ret})"); return r == ret; } catch { return false; }
        }

        /// <summary>F3: is the cursor on a detail?</summary>
        public static void ReadStatus()
        {
            var g = Game; var it = Item(out _);
            if (!_active || g == null || it == null) return;
            Speech.Say(Menus.Join(it.hitIdx >= 0 ? "On a detail" : "Not on a detail", $"{_spots.Count} details"), true, false);
            Main.Log.Msg($"3D status: hitIdx={it.hitIdx} cursor {g.C_Cur_dx >> 16},{g.C_Cur_dy >> 16} rot={it.totalRot}");
        }
    }
}
