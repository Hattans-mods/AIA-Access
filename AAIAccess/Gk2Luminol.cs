extern alias GK2;
using System;
using System.Collections.Generic;
using UnityEngine;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: the luminol test (cSceneLuminol). Modelled on the Phoenix Wright mod's luminol support:
    /// [ and ] put the cursor on each hidden trace, the player sprays with the game's own key, and the mod says how many
    /// are still to find. The game's traces are the check areas of the scene's map collision (mColl); a spray hits the
    /// area under the cursor, and a trace shows once it has been sprayed enough times (IsLuminolChecked).</summary>
    internal static class Gk2Luminol
    {
        private class Spot { public uint Label; public Vector2 Aim; public bool Found; public int Sprays; }

        private static readonly List<Spot> _spots = new List<Spot>();
        private static bool _active, _clear;
        private static int _index = -1, _diag, _lastOver = -2, _pos = -1;
        private static IntPtr _coll = IntPtr.Zero;
        private static float _next, _lostAt = -1, _arriveAt = -1;

        public static bool Active => _active;

        private static G2.cSceneLuminol Scene { get { try { return G2.cSceneLuminol.Instance; } catch { return null; } } }

        private static Il2CppGK_Common.UI.SousaPart.LuminolCursor Cursor(G2.cSceneLuminol s)
        {
            try { var c = s.m_Cursor; return c != null && !c.WasCollected && c.gameObject.activeInHierarchy ? c : null; } catch { return null; }
        }

        private static float Scale() { try { return Il2CppGK_System.Core.GKScreen.GetScreenScaleRate(); } catch { return 1080f / 192f; } }

        // The game's own conversion (procMain_Check): x = pos.x / scale, y = (1080 - pos.y) / scale, in the picture's units.
        private static Vector2 ToGame(Vector2 p) { float k = Scale(); return new Vector2(p.x / k, (1080f - p.y) / k); }
        private static Vector2 FromGame(Vector2 g) { float k = Scale(); return new Vector2(g.x * k, 1080f - g.y * k); }

        /// <summary>The trace the game would spray at this point (its own test), or -1.</summary>
        private static int HitAt(G2.cSceneLuminol s, Vector2 g)
        {
            try
            {
                G2.cFx32 x = new G2.cFx32(), y = new G2.cFx32(), z = new G2.cFx32();
                x.mVal = (int)(g.x * 4096); y.mVal = (int)(g.y * 4096); z.mVal = 0;
                var v = new G2.GKJ2Global.cVector3D(x, y, z);
                var r = s.mColl.getEventColl(G2.GKJ2Global.MAP_COLL_TYPE.MAP_COLL_TYPE_CHECK, v);
                if (r == null) return -1;
                var chk = (G2.GKJ2Global.MAP_COLL_CHECK)r;
                return chk == null ? -1 : (int)chk.ScriptLabelNo;
            }
            catch (Exception e) { if (_diag++ < 10) Main.Log.Warning("GK2 luminol hit test: " + e.Message); return -1; }
        }

        private static void Build(G2.cSceneLuminol s)
        {
            _spots.Clear(); _index = -1; _lastOver = -2; _arriveAt = -1;
            try
            {
                var co = s.mColl; var tbl = co.getCheckCollTbl(); int n = co.getCheckCollNum();
                var rects = new Dictionary<uint, List<(Vector2 a, Vector2 b)>>();
                for (int i = 0; tbl != null && i < n && i < tbl.Length; i++)
                {
                    var c = tbl[i]; if (c == null) continue;
                    bool on = true; try { on = co.isCollEnable(c.RefFlagNo); } catch { }
                    var a = new Vector2(Gk2Map.Fx(c.Start.x), Gk2Map.Fx(c.Start.y)); var b = new Vector2(Gk2Map.Fx(c.End.x), Gk2Map.Fx(c.End.y));
                    Main.Log.Msg($"GK2 luminol area {i}: label {c.ScriptLabelNo} {a}-{b} {c.Shape} enabled={on}");
                    if (!on) continue;
                    if (!rects.TryGetValue(c.ScriptLabelNo, out var l)) rects[c.ScriptLabelNo] = l = new List<(Vector2, Vector2)>();
                    l.Add((a, b));
                }
                foreach (var kv in rects)
                {
                    // Aim where the game's own test picks this trace, near the middle of those points.
                    var pts = new List<Vector2>();
                    foreach (var r in kv.Value)
                    {
                        float x0 = Math.Min(r.a.x, r.b.x), x1 = Math.Max(r.a.x, r.b.x), y0 = Math.Min(r.a.y, r.b.y), y1 = Math.Max(r.a.y, r.b.y);
                        float st = Math.Max(1f, Math.Min(x1 - x0, y1 - y0) / 8f);
                        for (float y = y0 + 0.5f; y < y1; y += st) for (float x = x0 + 0.5f; x < x1; x += st)
                            if (HitAt(s, new Vector2(x, y)) == kv.Key) pts.Add(new Vector2(x, y));
                    }
                    Vector2 aim;
                    if (pts.Count == 0) { var r = kv.Value[0]; aim = (r.a + r.b) / 2; Main.Log.Msg($"GK2 luminol: the game's test never picks label {kv.Key}; aiming at the middle"); }
                    else
                    {
                        var m = Vector2.zero; foreach (var p in pts) m += p; m /= pts.Count;
                        aim = pts[0]; float bd = float.MaxValue;
                        foreach (var p in pts) { float d = (p - m).sqrMagnitude; if (d < bd) { bd = d; aim = p; } }
                    }
                    _spots.Add(new Spot { Label = kv.Key, Aim = aim });
                }
                _spots.Sort((a, b) => Math.Abs(a.Aim.x - b.Aim.x) > 20 ? a.Aim.x.CompareTo(b.Aim.x) : a.Aim.y.CompareTo(b.Aim.y));
                Update(s, false);
                Main.Log.Msg($"GK2 luminol pos {_pos}: {_spots.Count} traces: " + string.Join("; ", _spots.ConvertAll(x => $"label {x.Label} aim {x.Aim.x:0},{x.Aim.y:0} found={x.Found} sprays={x.Sprays}")));
            }
            catch (Exception e) { Main.Log.Warning("GK2 luminol build: " + e); }
        }

        private static int Remaining() { int n = 0; foreach (var x in _spots) if (!x.Found) n++; return n; }

        /// <summary>Reads which traces have shown and how often each was sprayed; speaks what changed.</summary>
        private static void Update(G2.cSceneLuminol s, bool speak)
        {
            foreach (var sp in _spots)
            {
                bool found = false; int sprays = 0;
                try { found = s.IsLuminolChecked(sp.Label); } catch { }
                try { var hc = s.mHitCount; if (hc != null && sp.Label < hc.Length) sprays = hc[(int)sp.Label]; } catch { }
                if (speak && found && !sp.Found)
                {
                    int left = 0; foreach (var o in _spots) if (o != sp && !o.Found) left++;
                    Speech.Say(left == 0 ? "A trace showed up. That's all of them." : left == 1 ? "A trace showed up. 1 more to find." : $"A trace showed up. {left} more to find.", true, false);
                }
                else if (speak && sprays > sp.Sprays && !found) Speech.Say("Sprayed. Nothing yet, spray again.", true, false);
                sp.Found = found; sp.Sprays = sprays;
            }
        }

        public static void Poll()
        {
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.05f;
            var s = Gk2Map.InGame ? Scene : null;
            var c = s == null ? null : Cursor(s);
            if (c == null)
            {
                if (_active) { if (_lostAt < 0) _lostAt = now; else if (now - _lostAt > 0.8f) { _active = false; Main.Log.Msg("GK2 luminol closed"); } }
                return;
            }
            _lostAt = -1;
            try
            {
                int pos = -1; try { pos = (int)s.mLuminolPos; } catch { }
                IntPtr coll = IntPtr.Zero; try { coll = s.mColl.Pointer; } catch { }
                if (!_active || pos != _pos || coll != _coll)
                {
                    bool fresh = !_active;
                    _active = true; _pos = pos; _coll = coll; _clear = false;
                    Build(s);
                    int left = Remaining();
                    Speech.Say(Menus.Join(fresh ? "Luminol test" : "New spot to test",
                        _spots.Count == 0 ? "no traces here" : left == 0 ? "all traces found" : left == 1 ? "1 trace to find" : $"{left} traces to find",
                        fresh ? "bracket keys move to each one, then spray" : null), false, false);
                }
                Update(s, true);
                var g = ToGame(c.Position);
                int over = HitAt(s, g);
                if (over != _lastOver)
                {
                    if (over >= 0 && _arriveAt < 0) { var sp = _spots.Find(x => x.Label == over); Earcons.Play(sp != null && sp.Found ? Earcons.SpotChecked : Earcons.Spot); }
                    _lastOver = over;
                }
                if (_arriveAt > 0 && now >= _arriveAt)
                {
                    _arriveAt = -1;
                    if (_index >= 0 && _index < _spots.Count)
                    {
                        var sp = _spots[_index];
                        Main.Log.Msg($"GK2 luminol cursor arrived {g}: on label {over}, want {sp.Label}");
                        string d = $"Trace {_index + 1} of {_spots.Count}";
                        if (over == (int)sp.Label) Speech.Say(sp.Found ? d + ", already found" : d + ". Spray now.", true, false);
                        else Speech.Say(d + ": the cursor didn't land on it.", true, false);
                    }
                }
                bool clear = false; try { clear = s.isGameClear(); } catch { }
                if (clear && !_clear) Main.Log.Msg("GK2 luminol: all found");
                _clear = clear;
            }
            catch (Exception e) { if (_diag++ < 20) Main.Log.Warning("GK2 luminol poll: " + e.Message); }
        }

        public static void Step(int dir)
        {
            try
            {
                var s = Scene; var c = s == null ? null : Cursor(s);
                if (c == null || _spots.Count == 0) { Speech.Say("No traces here.", true, false); return; }
                _index = _index < 0 ? (dir > 0 ? 0 : _spots.Count - 1) : (_index + dir + _spots.Count) % _spots.Count;
                var sp = _spots[_index];
                var p = FromGame(sp.Aim);
                c.SetPosition(p.x, p.y);
                _arriveAt = Time.unscaledTime + 0.2f;
                Main.Log.Msg($"GK2 luminol cursor to label {sp.Label} at {sp.Aim} -> {p}");
                Speech.Stop();
            }
            catch (Exception e) { Main.Log.Error("GK2 luminol step: " + e); }
        }

        public static void ReadStatus()
        {
            try
            {
                var s = Scene; var c = s == null ? null : Cursor(s); if (c == null) return;
                Update(s, false);
                var g = ToGame(c.Position); int over = HitAt(s, g);
                int idx = _spots.FindIndex(x => x.Label == over);
                int left = Remaining();
                Speech.Say(Menus.Join(left == 0 ? "All traces found" : $"{_spots.Count - left} of {_spots.Count} traces found",
                    idx < 0 ? "cursor not on a trace" : $"cursor on trace {idx + 1}" + (_spots[idx].Found ? ", found" : "")), true, false);
                Main.Log.Msg($"GK2 luminol status: cursor {c.Position} = {g}, over {over}, pos {_pos}");
            }
            catch (Exception e) { Main.Log.Error("GK2 luminol status: " + e.Message); }
        }
    }
}
