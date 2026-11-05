extern alias GK2;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: dusting for fingerprints (cSceneFingerPrint). Modelled on the Phoenix Wright mod's fingerprint
    /// support. The game keeps a 1920 by 1080 map of where the print is (mpMaskBuf, above mMaskDot) and of where powder lies
    /// (mpBmpBuf, above mClearValue). It succeeds when enough of the print is powdered and no loose powder is left
    /// anywhere else (cheackClearFinger). The mod reads those maps: [ and ] put the cursor on the parts of the print still
    /// bare, and after each drop or blow it says how much of the print is covered and whether loose powder is left.</summary>
    internal static class Gk2Finger
    {
        private const int W = 1920, H = 1080;
        private static float _radius = 150;

        private static bool _active, _done;
        private static byte[] _mask, _pow;
        private static int _printPixels, _need, _diag, _index = -1;
        private static int _x0, _y0, _x1, _y1;            // the print's bounding box (buffer pixels, y down)
        private static int _lastCovered = -1, _lastLoose = -1, _lastState = -1;
        private static float _next, _lostAt = -1, _readAt;
        private static List<Vector2> _bare = new List<Vector2>();

        public static bool Active => _active;

        private static G2.cSceneFingerPrint Scene { get { try { return G2.cSceneFingerPrint.Instance; } catch { return null; } } }

        private static Il2CppGK_Common.UI.SousaPart.FingerprintCursor Cursor(G2.cSceneFingerPrint s)
        {
            try { var c = s.m_Cursor; return c != null && !c.WasCollected && c.gameObject.activeInHierarchy ? c : null; } catch { return null; }
        }

        private static bool Copy(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> a, ref byte[] into)
        {
            if (a == null) return false;
            int n = a.Length; if (n < W * H) return false;
            if (into == null || into.Length != n) into = new byte[n];
            Marshal.Copy(a.Pointer + 0x20, into, 0, n);   // IL2CPP arrays: elements start 0x20 bytes in
            return true;
        }

        private static void ReadPrint(G2.cSceneFingerPrint s)
        {
            _printPixels = 0; _x0 = W; _y0 = H; _x1 = -1; _y1 = -1;
            if (!Copy(s.mpMaskBuf, ref _mask)) { Main.Log.Warning("GK2 fingerprint: no print map"); return; }
            byte dot = s.mMaskDot;
            for (int y = 0; y < H; y++)
                for (int x = 0, o = y * W; x < W; x++, o++)
                    if (_mask[o] > dot) { _printPixels++; if (x < _x0) _x0 = x; if (x > _x1) _x1 = x; if (y < _y0) _y0 = y; if (y > _y1) _y1 = y; }
            // How big one drop is: the game's powder pattern (512 by 512, centred on the cursor); its powdered area as a circle.
            _radius = 150;
            try
            {
                byte[] pat = null;
                var pb = s.mpPowderBuf;
                if (pb != null && pb.Length >= 512 * 512)
                {
                    pat = new byte[512 * 512]; Marshal.Copy(pb.Pointer + 0x20, pat, 0, pat.Length);
                    int nz = 0; foreach (var b in pat) if (b != 0) nz++;
                    if (nz > 1000) _radius = (float)(Math.Sqrt(nz / Math.PI) * 0.85);
                    Main.Log.Msg($"GK2 fingerprint: one drop powders {nz} pixels, aiming with radius {_radius:0}");
                }
            }
            catch (Exception e) { Main.Log.Warning("GK2 fingerprint powder pattern: " + e.Message); }
            _need = 0;
            float rate = 1;
            try { rate = s.m_Cursor.PassingRate; } catch { }
            try { var t = G2.cSceneFingerPrint.finger_tbl; if (t != null && t.Length > 0) _need = (int)(t[0].min_cnt * rate); } catch (Exception e) { Main.Log.Warning("GK2 fingerprint table: " + e.Message); }
            Main.Log.Msg($"GK2 fingerprint: print {_printPixels} pixels in {_x0},{_y0}-{_x1},{_y1}; need {_need} (rate {rate}); maskDot {dot} clear {s.mClearValue}");
        }

        /// <summary>Covered print pixels, loose powder pixels, and the bare parts of the print (cell middles).</summary>
        private static (int covered, int loose) ReadPowder(G2.cSceneFingerPrint s)
        {
            if (_mask == null || !Copy(s.mpBmpBuf, ref _pow)) return (-1, -1);
            byte dot = s.mMaskDot, clr = s.mClearValue;
            int covered = 0, loose = 0;
            const int B = 16; int bw = W / B + 1, bh = H / B + 1;
            var bare = new int[bw * bh];
            for (int y = 0; y < H; y++)
                for (int x = 0, o = y * W; x < W; x++, o++)
                {
                    if (_mask[o] > dot) { if (_pow[o] > clr) covered++; else bare[(y / B) * bw + x / B]++; }
                    else if (_pow[o] != 0) loose++;
                }
            // As few drop points as cover the bare print: each drop powders a circle of radius _radius.
            // Greedy: the point that covers the most bare print, then the next, until little is left.
            _bare.Clear();
            int left = 0; foreach (var v in bare) left += v;
            int stopAt = Math.Max(200, _printPixels / 50);
            int rb = Math.Max(1, (int)(_radius / B));
            int bx0 = Math.Max(0, _x0 / B), bx1 = Math.Min(bw - 1, _x1 / B), by0 = Math.Max(0, _y0 / B), by1 = Math.Min(bh - 1, _y1 / B);
            while (left > stopAt && _bare.Count < 12)
            {
                int best = 0, bcx = 0, bcy = 0;
                for (int cy = by0; cy <= by1; cy += 2)
                    for (int cx = bx0; cx <= bx1; cx += 2)
                    {
                        int sum = 0;
                        for (int yy = Math.Max(0, cy - rb); yy <= Math.Min(bh - 1, cy + rb); yy++)
                            for (int xx = Math.Max(0, cx - rb); xx <= Math.Min(bw - 1, cx + rb); xx++)
                                if ((xx - cx) * (xx - cx) + (yy - cy) * (yy - cy) <= rb * rb) sum += bare[yy * bw + xx];
                        if (sum > best) { best = sum; bcx = cx; bcy = cy; }
                    }
                if (best < stopAt / 2) break;
                for (int yy = Math.Max(0, bcy - rb); yy <= Math.Min(bh - 1, bcy + rb); yy++)
                    for (int xx = Math.Max(0, bcx - rb); xx <= Math.Min(bw - 1, bcx + rb); xx++)
                        if ((xx - bcx) * (xx - bcx) + (yy - bcy) * (yy - bcy) <= rb * rb) { left -= bare[yy * bw + xx]; bare[yy * bw + xx] = 0; }
                _bare.Add(new Vector2(bcx * B + B / 2, bcy * B + B / 2));
            }
            _bare.Sort((a, b) => Math.Abs(a.y - b.y) > _radius / 2 ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            return (covered, loose);
        }

        private static int Percent(int covered) => _printPixels <= 0 ? 0 : (int)Math.Round(covered * 100.0 / _printPixels);

        private static string NeedText() => _need > 0 && _printPixels > 0 ? $"need {Math.Min(100, (int)Math.Ceiling(_need * 100.0 / _printPixels))}" : null;

        public static void Poll()
        {
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.1f;
            var s = Gk2Map.InGame ? Scene : null;
            var c = s == null ? null : Cursor(s);
            if (c == null)
            {
                if (_active) { if (_lostAt < 0) _lostAt = now; else if (now - _lostAt > 0.8f) { _active = false; _mask = _pow = null; Main.Log.Msg("GK2 fingerprint closed"); } }
                return;
            }
            _lostAt = -1;
            try
            {
                if (!_active)
                {
                    _active = true; _done = false; _index = -1; _lastCovered = _lastLoose = _lastState = -1;
                    ReadPrint(s);
                    var r = ReadPowder(s); _lastCovered = r.covered; _lastLoose = r.loose;
                    Speech.Say(_printPixels == 0 ? "Fingerprint dusting. The mod couldn't find the print."
                        : $"Fingerprint dusting. {_bare.Count} places to powder; bracket keys move the cursor to each. Drop powder a couple of times at each, then blow the loose powder away.", false, false);
                }
                int state = -1; try { state = (int)s.mState; } catch { }
                // After a drop or a blow has finished (the game goes back to idle), say how it looks now.
                if (state == 0 && _lastState != 0 && _lastState != -1) _readAt = now + 0.3f;
                if (state == 3 && !_done) { _done = true; Speech.Say("The print is showing!", true, false); Main.Log.Msg("GK2 fingerprint: success"); }
                _lastState = state;
                if (_readAt > 0 && now >= _readAt && state == 0)
                {
                    _readAt = -1;
                    var r = ReadPowder(s);
                    if (r.covered != _lastCovered || r.loose != _lastLoose)
                    {
                        Speech.Say(Menus.Join($"Print {Percent(r.covered)} percent covered", NeedText(), Loose(r.loose)), true, false);
                        Main.Log.Msg($"GK2 fingerprint: covered {r.covered}/{_printPixels} ({Percent(r.covered)}%), loose {r.loose}, score {s.mScore} useless {s.mUseless}, bare parts {_bare.Count}");
                    }
                    _lastCovered = r.covered; _lastLoose = r.loose; _index = -1;
                }
            }
            catch (Exception e) { if (_diag++ < 20) Main.Log.Warning("GK2 fingerprint poll: " + e.Message); }
        }

        private static string Loose(int loose) => loose <= 0 ? "no loose powder" : loose < 2000 ? "a little loose powder left, blow again" : "loose powder around it, blow it away";

        public static void Step(int dir)
        {
            try
            {
                var s = Scene; var c = s == null ? null : Cursor(s); if (c == null) return;
                if (_printPixels == 0) { Speech.Say("The mod couldn't find the print.", true, false); return; }
                ReadPowder(s);
                if (_bare.Count == 0) { Speech.Say("The print is covered. Blow away any loose powder.", true, false); return; }
                _index = _index < 0 ? (dir > 0 ? 0 : _bare.Count - 1) : (_index + dir + _bare.Count) % _bare.Count;
                var p = _bare[_index];
                // The cursor works in screen pixels with y going up; the maps have y going down.
                c.SetPosition(p.x, H - p.y);
                Main.Log.Msg($"GK2 fingerprint cursor to bare part {_index + 1}/{_bare.Count} at {p}");
                Speech.Say($"Drop point {_index + 1} of {_bare.Count}, {Where(p)}", true, false);
            }
            catch (Exception e) { Main.Log.Error("GK2 fingerprint step: " + e); }
        }

        /// <summary>Where on the print: by thirds of its bounding box.</summary>
        private static string Where(Vector2 p)
        {
            float fx = (p.x - _x0) / Math.Max(1, _x1 - _x0), fy = (p.y - _y0) / Math.Max(1, _y1 - _y0);
            string v = fy < 1 / 3f ? "top" : fy > 2 / 3f ? "bottom" : "", h = fx < 1 / 3f ? "left" : fx > 2 / 3f ? "right" : "";
            string d = v.Length > 0 && h.Length > 0 ? v + " " + h : v + h;
            return d.Length == 0 ? "middle" : d;
        }

        public static void ReadStatus()
        {
            try
            {
                var s = Scene; var c = s == null ? null : Cursor(s); if (c == null) return;
                var r = ReadPowder(s);
                var cp = c.Position; var p = new Vector2(cp.x, H - cp.y);
                int o = (int)p.y * W + (int)p.x;
                bool onPrint = _mask != null && o >= 0 && o < _mask.Length && _mask[o] > s.mMaskDot;
                bool nearPrint = p.x >= _x0 - 40 && p.x <= _x1 + 40 && p.y >= _y0 - 40 && p.y <= _y1 + 40;
                Speech.Say(Menus.Join($"Print {Percent(r.covered)} percent covered", NeedText(), Loose(r.loose),
                    _bare.Count == 0 ? null : _bare.Count == 1 ? "1 place still to powder" : $"{_bare.Count} places still to powder",
                    onPrint ? "cursor on the print" : nearPrint ? "cursor near the print" : "cursor away from the print"), true, false);
                Main.Log.Msg($"GK2 fingerprint status: cursor {cp} -> {p}, covered {r.covered}/{_printPixels}, loose {r.loose}, state {s.mState}, score {s.mScore}, useless {s.mUseless}");
            }
            catch (Exception e) { Main.Log.Error("GK2 fingerprint status: " + e.Message); }
        }
    }
}
