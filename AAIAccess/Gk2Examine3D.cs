extern alias GK2;
using System;
using System.Collections.Generic;
using UnityEngine;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: 3D evidence examination. The game's own detail table (ITEM3D_INFO.pHitInfo) lists each
    /// detail with the angle and zoom the game turns to when it is found. [ and ] turn the evidence to that angle
    /// (the game's own reqAutoZoom), then search the screen with the game's hit test and put the cursor on it.</summary>
    internal static class Gk2Examine3D
    {
        private class Spot { public int Index; public G2.GKJ2Global.ITEM3D_HIT_RET Ret; public IntPtr Ptr; public string Poly = ""; public bool Turnable; }

        private static readonly List<Spot> _spots = new List<Spot>();
        private static bool _active;
        private static IntPtr _info = IntPtr.Zero;
        private static int _index = -1, _lastOver = -2, _diag;
        private static float _lostAt = -1, _searchAt = -1, _arriveAt = -1, _nextPoll;
        private static readonly HashSet<int> _unfound = new HashSet<int>();
        private static int _tries, _hitIdx = -1, _alt = -1;
        private static float _turnAt;
        private static (int, int, int, int) _lastQuat;

        private static (int, int, int, int) Quat(G2.cItem3DModel m)
        {
            try { var q = m.quaternion(); return (q.x, q.y, q.z, q.w); } catch { return (0, 0, 0, 0); }
        }

        public static bool Active => _active;

        private static G2.cSceneItem3D Scene { get { try { return G2.cSceneItem3D.Instance; } catch { return null; } } }

        private static G2.cItem3DTaskCursor Cursor(G2.cSceneItem3D s) { try { return s.getCursor(); } catch { return null; } }

        private static bool Shown(G2.cItem3DTaskCursor c)
        {
            try { var o = c?.m_GKCursorObj; return o != null && !o.WasCollected && o.m_IsDisplayed; } catch { return false; }
        }

        private static bool OnChecked(G2.cItem3DTaskCursor c)
        {
            try { var o = c.m_GKCursorObj; return o.m_CheckMarkObj != null && o.m_CheckMarkObj.activeInHierarchy; } catch { return false; }
        }

        private static G2.GKJ2Global.ITEM3D_HIT_RET HitAt(G2.cItem3DModel m, float x, float y)
        {
            try { G2.cFx32 a = new G2.cFx32(), b = new G2.cFx32(); a.mVal = (int)(x * 4096); b.mVal = (int)(y * 4096); return m.hitCheck(a, b); } catch { return null; }
        }

        private static int SpotOf(G2.GKJ2Global.ITEM3D_HIT_RET r)
        {
            if (r == null) return -1;
            for (int i = 0; i < _spots.Count; i++) if (_spots[i].Ptr == r.Pointer) return i;
            return -1;
        }

        private static void Build(G2.cSceneItem3D s)
        {
            _spots.Clear(); _index = -1; _lastOver = -2; _unfound.Clear();
            try
            {
                var info = s.mpInfo; var m = s.model();
                var list = info.pHitInfo;
                int n = list.Count;
                int idx = 0; try { idx = (int)m.getHitIndex(); } catch { }
                Main.Log.Msg($"GK2 3D: item {info.itemNo} hitNum={info.hitNum} infos={n} hitIdx={info.hitIdx} model idx={idx}");
                // Use the table the model is set to (falls back to the first).
                var hi = idx >= 0 && idx < n ? list[idx] : list[0];
                _hitIdx = idx;
                var res = hi.pResult; var polys = hi.pHitPoly;
                // The result table is shared by all the evidence's states; only the entries some part of the model points
                // at (each hit polygon's retNo is an index into it) can be found in the state it is in now.
                var used = new SortedSet<int>();
                for (int i = 0; polys != null && i < polys.Count; i++) { int k = (int)polys[i].retNo; if (k >= 0 && k < res.Count) used.Add(k); }
                for (int i = 0; i < res.Count; i++)
                {
                    var r = res[i]; if (r == null) continue;
                    if (used.Count > 0 && !used.Contains(i)) continue;
                    var sp = new Spot { Index = i, Ret = r, Ptr = r.Pointer, Turnable = r.quatX != 0 || r.quatY != 0 || r.quatZ != 0 || r.quatW != 0 };
                    _spots.Add(sp);
                }
                for (int i = 0; polys != null && i < polys.Count; i++)
                {
                    var p = polys[i];
                    Main.Log.Msg($"  poly {p.no} ret={p.retNo} '{p.hitPolyName}'");
                }
                foreach (var sp in _spots)
                    Main.Log.Msg($"  detail {sp.Index} (spot {_spots.IndexOf(sp) + 1}): spt={sp.Ret.spt} label={sp.Ret.label} flag={sp.Ret.flag} pos={sp.Ret.posX},{sp.Ret.posY},{sp.Ret.posZ} quat={sp.Ret.quatX},{sp.Ret.quatY},{sp.Ret.quatZ},{sp.Ret.quatW}");
            }
            catch (Exception e) { Main.Log.Warning("GK2 3D build: " + e); }
        }

        public static void Poll()
        {
            float now = Time.unscaledTime;
            if (now < _nextPoll) return;
            _nextPoll = now + 0.05f;
            var s = Gk2Map.InGame ? Scene : null;
            G2.cItem3DTaskCursor c = null; bool shown = false;
            try { if (s != null && s.getMode() == G2.GKJ2Global.ITEM3D_MODE.ITEM3D_MODE_CHECK) { c = Cursor(s); shown = Shown(c); } } catch { }
            if (!shown)
            {
                if (_active) { if (_lostAt < 0) _lostAt = now; else if (now - _lostAt > 0.8f) { _active = false; _searchAt = _arriveAt = -1; Main.Log.Msg("GK2 3D examination closed"); } }
                return;
            }
            _lostAt = -1;
            try
            {
                IntPtr info = IntPtr.Zero; try { info = s.mpInfo.Pointer; } catch { }
                int hidx = -1; try { hidx = (int)s.model().getHitIndex(); } catch { }
                if (_active && info == _info && hidx != _hitIdx && _searchAt < 0 && _arriveAt < 0)
                {
                    // The evidence changed state (opened, turned over...): other details can be found now.
                    Build(s);
                    Speech.Say(_spots.Count == 1 ? "The evidence changed. 1 detail now" : $"The evidence changed. {_spots.Count} details now", false, false);
                }
                if (!_active || info != _info)
                {
                    bool fresh = !_active;
                    _active = true; _info = info; Build(s);
                    try { Main.Log.Msg($"GK2 3D cursor: pos={Gk2Map.V2(c.mPos)} area={c.m_AreaX},{c.m_AreaY},{c.m_AreaW},{c.m_AreaH}"); } catch { }
                    Speech.Say(Menus.Join(fresh ? "Examining in 3D" : "New evidence", _spots.Count == 1 ? "1 detail" : _spots.Count + " details",
                        "bracket keys turn it to each detail"), false, false);
                }
                var m = s.model();
                // A tone when the cursor is over a detail.
                if (_searchAt < 0)
                {
                    var p = c.mPos; int over = SpotOf(HitAt(m, Gk2Map.Fx(p.x), Gk2Map.Fx(p.y)));
                    if (over != _lastOver) { if (over >= 0 && _arriveAt < 0) Earcons.Play(OnChecked(c) ? Earcons.SpotChecked : Earcons.Spot); _lastOver = over; }
                }
                if (_searchAt > 0 && now >= _searchAt)
                {
                    // Search as soon as the turn has finished (the evidence stops moving), not after a fixed wait.
                    var q = Quat(m);
                    if (q != _lastQuat && now - _turnAt < 2f) { _lastQuat = q; _searchAt = now + 0.05f; }
                    else { _searchAt = -1; Search(s, c); }
                }
                if (_arriveAt > 0 && now >= _arriveAt)
                {
                    _arriveAt = -1;
                    var p = c.mPos; int at = SpotOf(HitAt(m, Gk2Map.Fx(p.x), Gk2Map.Fx(p.y)));
                    Main.Log.Msg($"GK2 3D cursor arrived {Gk2Map.V2(p)}: on detail {at}, want {_index}, checked={OnChecked(c)}");
                    string d = $"Detail {_index + 1} of {_spots.Count}";
                    if (at == _index) Speech.Say(OnChecked(c) ? d + ", checked" : d, true, false);
                    else Speech.Say(d + ": the cursor didn't land on it. Try turning a little.", true, false);
                }
            }
            catch (Exception e) { if (_diag++ < 20) Main.Log.Warning("GK2 3D poll: " + e.Message); }
        }

        public static void Step(int dir)
        {
            try
            {
                var s = Scene; if (s == null || _spots.Count == 0) { Speech.Say("No details here.", true, false); return; }
                // Details that couldn't be found are skipped from then on (until the evidence changes).
                if (_unfound.Count >= _spots.Count) { Speech.Say("None of the details can be found right now.", true, false); return; }
                do _index = _index < 0 ? (dir > 0 ? 0 : _spots.Count - 1) : (_index + dir + _spots.Count) % _spots.Count;
                while (_unfound.Contains(_index));
                _alt = -1;
                if (!_spots[_index].Turnable)
                {
                    // No angle of its own: look for it as the evidence is now (then from the other details' angles).
                    _tries = 3; _turnAt = -10; _searchAt = Time.unscaledTime; _arriveAt = -1;
                    Main.Log.Msg($"GK2 3D detail {_index} has no angle of its own; searching as it is");
                    Speech.Stop();
                    return;
                }
                TurnTo(s, _spots[_index].Ret);
                _tries = 0; _turnAt = Time.unscaledTime; _searchAt = _turnAt + 0.08f; _arriveAt = -1; _lastQuat = (0, 0, 0, 0);
                Main.Log.Msg($"GK2 3D turn to detail {_index}");
                Speech.Stop();
            }
            catch (Exception e) { Main.Log.Error("GK2 3D step: " + e); }
        }

        private static void TurnTo(G2.cSceneItem3D s, G2.GKJ2Global.ITEM3D_HIT_RET r)
        {
            {
                // Turn and zoom the evidence the way the game does when this detail is found.
                var q = new G2.cQuaternion(r.quatX, r.quatY, r.quatZ, r.quatW);
                G2.cFx32 fx = new G2.cFx32(), fy = new G2.cFx32(), fz = new G2.cFx32();
                fx.mVal = r.posX; fy.mVal = r.posY; fz.mVal = r.posZ;
                var v = new G2.GKJ2Global.cVector3D(fx, fy, fz);
                var mdl = s.model();
                mdl.reqAutoZoom(q, v);
                // The game turns it a fixed step each frame (its progress, mAutoRate, goes up by 0x66 out of 0x1000,
                // about 40 frames). Set the progress to all but the last step: the next frame finishes the turn,
                // at exactly the angle and position the game would end on.
                try { System.Runtime.InteropServices.Marshal.WriteInt32(mdl.Pointer + 0x100, 0x1000 - 0x66); } catch { }
            }
        }

        /// <summary>After the turn: scan the screen with the game's hit test for the wanted detail, aim at its middle.</summary>
        private static void Search(G2.cSceneItem3D s, G2.cItem3DTaskCursor c)
        {
            var m = s.model();
            int x0 = 8, y0 = 8, x1 = 333, y1 = 184;
            try { if (c.m_AreaW > 0) { x0 = c.m_AreaX + 4; y0 = c.m_AreaY + 4; x1 = c.m_AreaX + c.m_AreaW - 4; y1 = c.m_AreaY + c.m_AreaH - 4; } } catch { }
            var pts = new List<Vector2>(); var seen = new Dictionary<int, int>();
            for (int y = y0; y <= y1; y += 4)
                for (int x = x0; x <= x1; x += 4)
                {
                    int k = SpotOf(HitAt(m, x, y));
                    if (k >= 0) { seen[k] = seen.TryGetValue(k, out var cnt) ? cnt + 1 : 1; if (k == _index) pts.Add(new Vector2(x, y)); }
                }
            Main.Log.Msg($"GK2 3D search for {_index}: {pts.Count} hits; visible: {string.Join(", ", new List<string>(Enum(seen)))}");
            if (pts.Count == 0)
            {
                if (++_tries < 4) { _searchAt = Time.unscaledTime + 0.2f; _turnAt = -10; return; }
                // Not from its own angle: try the angles of the other details.
                while (++_alt < _spots.Count)
                {
                    var a = _spots[_alt];
                    if (_alt == _index || !a.Turnable) continue;
                    try { TurnTo(s, a.Ret); } catch { continue; }
                    Main.Log.Msg($"GK2 3D looking for detail {_index} from detail {_alt}'s angle");
                    _tries = 2; _turnAt = Time.unscaledTime; _searchAt = _turnAt + 0.08f; _lastQuat = (0, 0, 0, 0);
                    return;
                }
                _unfound.Add(_index);
                Speech.Say($"Couldn't find detail {_index + 1} on screen. Skipping it from now on.", true, false); return;
            }
            // The hit point nearest the middle of all hits (stays inside odd shapes).
            Vector2 mid = Vector2.zero; foreach (var p in pts) mid += p; mid /= pts.Count;
            Vector2 best = pts[0]; float bd = float.MaxValue;
            foreach (var p in pts) { float d = (p - mid).sqrMagnitude; if (d < bd) { bd = d; best = p; } }
            // Put the cursor straight there (its position, in the game's fixed-point units), then tell the game that's
            // its target too, so it doesn't glide anywhere else.
            try
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(c.Pointer + 0x68, (int)best.x * 4096);
                System.Runtime.InteropServices.Marshal.WriteInt32(c.Pointer + 0x6c, (int)best.y * 4096);
            }
            catch { }
            c.SetTargetMove((ushort)best.x, (ushort)best.y);
            _arriveAt = Time.unscaledTime + 0.04f;
            Main.Log.Msg($"GK2 3D cursor to {best.x},{best.y}");
        }

        private static IEnumerable<string> Enum(Dictionary<int, int> d) { foreach (var kv in d) yield return $"{kv.Key}:{kv.Value}"; }

        public static void ReadStatus()
        {
            try
            {
                var s = Scene; var c = s == null ? null : Cursor(s); if (c == null) { Speech.Say("No cursor.", true, false); return; }
                var p = c.mPos; int at = SpotOf(HitAt(s.model(), Gk2Map.Fx(p.x), Gk2Map.Fx(p.y)));
                string where = $"{(int)(Gk2Map.Fx(p.x) * 100 / 341)} percent across, {(int)(Gk2Map.Fx(p.y) * 100 / 192)} percent down";
                Speech.Say(Menus.Join("Cursor " + where, at < 0 ? "not on a detail" : $"on detail {at + 1}" + (OnChecked(c) ? ", checked" : ""), _spots.Count + " details"), true, false);
            }
            catch (Exception e) { Main.Log.Error("GK2 3D status: " + e.Message); }
        }
    }
}
