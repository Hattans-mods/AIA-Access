extern alias GK2;
using System;
using System.Collections.Generic;
using UnityEngine;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game: list the people and things in a room ([ and ]), walk to them (\), and say where they are (').
    /// The map is read from the game's own collision data: wall lines (ADJUST), examine areas (CHECK) and people.</summary>
    internal static class Gk2Nav
    {
        private class Target
        {
            public string Key;          // stays the same while the room does
            public string Name;
            public bool Person;
            public Vector2 Pos;         // a person's position, or the middle of an examine area
            public List<Area> Areas = new List<Area>();
            public uint Label;
            public int Res, NameId;     // people
        }
        private class Area { public Vector2 A, B; public string Shape; public bool Foot; }
        private struct Seg { public Vector2 A, B; public bool Gate; }

        private static readonly List<Target> _targets = new List<Target>();
        private static readonly List<Seg> _walls = new List<Seg>();
        private static bool _baseOnly;   // plan as if every wall the story switches on and off were open
        private static string _sel;
        private static string _map = "";
        private static float _minX, _minY, _maxX, _maxY;
        private static readonly HashSet<string> _checked = new HashSet<string>();
        private static Dictionary<string, string> _names;
        private static string _lastPrompt;

        /// <summary>The game's map data files are named "map00" or "map09_01": MAP00_00 uses map00 unless map00_00 exists.</summary>
        private static string NameFor(string key)
        {
            string id = _map.ToLowerInvariant();               // "map09_01"
            return LookupName(id + ":" + key) ?? (id.Length > 5 ? LookupName(id.Substring(0, 5) + ":" + key) : null);
        }

        internal static string LookupName(string fullKey)
        {
            if (_names == null)
            {
                _names = new Dictionary<string, string>();
                try
                {
                    using var st = typeof(Gk2Nav).Assembly.GetManifestResourceStream("gk2names.txt");
                    using var rd = new System.IO.StreamReader(st);
                    string line;
                    while ((line = rd.ReadLine()) != null)
                    {
                        line = line.Trim(); if (line.Length == 0 || line[0] == '#') continue;
                        int e = line.IndexOf('='); if (e > 0) _names[line.Substring(0, e).Trim().ToLowerInvariant()] = line.Substring(e + 1).Trim();
                    }
                }
                catch (Exception e) { Main.Log.Warning("gk2names: " + e.Message); }
            }
            fullKey = fullKey.ToLowerInvariant();
            if (fullKey.StartsWith("upcut_id_")) fullKey = "cut" + fullKey.Substring(9);   // UPCUT_ID_00_00A -> cut00_00a
            if (_names.TryGetValue(fullKey, out var n)) return n;
            // A variant picture (e.g. cut00_006_tuto:0) uses the original's names.
            var m = System.Text.RegularExpressions.Regex.Match(fullKey, "^(cut[0-9a-f]{2}_[0-9a-f]{3})_[a-z_]+(:.*)$");
            return m.Success && _names.TryGetValue(m.Groups[1].Value + m.Groups[2].Value, out n) ? n : null;
        }

        private static bool IsChecked(Target t) => _checked.Contains(_map + ":" + t.Key);

        /// <summary>The game's prompt says "checked" for whatever Edgeworth is facing: remember that for the list.</summary>
        // ---------- learning names from conversations ----------
        private static Target _talk, _conv;
        private static float _talkLostAt = -10f;
        private static readonly List<string> _convOrder = new List<string>();
        private static readonly Dictionary<string, int> _convCount = new Dictionary<string, int>();

        /// <summary>Someone spoke (called on each change of speaker). Right after Edgeworth starts talking to a person,
        /// the conversation's speakers are collected to find out that person's name.</summary>
        public static void HeardSpeaker(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            // The talk prompt usually goes away while people speak, but in some rooms it stays up the whole time.
            bool facingTalk = false; try { var cp = Investigation.CurrentPrompt; facingTalk = cp != null && cp.StartsWith("Talk"); } catch { }
            if (_conv == null && _talk != null && (Time.unscaledTime - _talkLostAt < 4f || facingTalk))
            {
                _conv = _talk; _convOrder.Clear(); _convCount.Clear();
                Main.Log.Msg($"GK2 conversation with {_conv.Key} ({_conv.Name})");
            }
            if (_conv == null) return;
            if (!_convCount.ContainsKey(name)) _convOrder.Add(name);
            _convCount[name] = (_convCount.TryGetValue(name, out int n) ? n : 0) + 1;
        }

        private static string Base(string n) => System.Text.RegularExpressions.Regex.Replace(n ?? "", @" \d+$", "");

        private static void EndConversation()
        {
            var t = _conv; _conv = null;
            if (t == null || !t.Person) return;
            string resName = ""; try { resName = ((G2.MAPTASK_RES_ID)t.Res).ToString(); } catch { }
            if (resName.StartsWith("MAPTASK_RES_ID_")) resName = resName.Substring(15);
            if (People.ContainsKey(resName)) return;   // already known by name
            string partner = "";
            try { var pa = Gk2Map.Ctrl?.taskCtrl()?.getPartner(); if (pa != null) partner = PersonName(pa.getResID().ToString(), (int)pa.getResID(), (int)pa.getNameID()); } catch { }
            bool Skip(string sp) => sp == "Edgeworth" || sp == partner || sp.Contains("?")
                || _targets.Exists(o => o != t && o.Person && string.Equals(Base(o.Name), sp, StringComparison.OrdinalIgnoreCase));
            int num = PersonNum(t.Res, t.NameId);
            string had = ObjNames.LearnedName(num);
            Main.Log.Msg($"GK2 conversation with {t.Key} over; speakers {string.Join(", ", _convOrder.ConvertAll(x => x + ":" + _convCount[x]))}");
            if (had != null && _convCount.ContainsKey(had)) return;
            string pick = null;
            int unknownAt = _convOrder.FindIndex(n => n.Contains("?"));
            if (unknownAt >= 0)
            {
                for (int i = unknownAt + 1; i < _convOrder.Count && pick == null; i++) if (!Skip(_convOrder[i])) pick = _convOrder[i];
            }
            else
            {
                int best = 0;
                foreach (var sp in _convOrder) if (!Skip(sp) && _convCount[sp] > best) { best = _convCount[sp]; pick = sp; }
                // Too many people talking: can't tell who this one is.
                if (_convOrder.FindAll(x => !Skip(x)).Count > 3) pick = null;
            }
            if (pick != null) ObjNames.Learn(num, pick);
        }

        private static void TrackChecked()
        {
            string pr = null;
            try { pr = Investigation.CurrentPrompt; } catch { }
            if (pr == _lastPrompt) return;
            if (_lastPrompt != null && _lastPrompt.StartsWith("Talk") && string.IsNullOrEmpty(pr)) _talkLostAt = Time.unscaledTime;
            // Back in control and facing something again: a conversation that was going on is over.
            if (!string.IsNullOrEmpty(pr) && _conv != null) EndConversation();
            _lastPrompt = pr;
            if (!string.IsNullOrEmpty(pr) && pr.StartsWith("Talk"))
            {
                try
                {
                    var pl0 = Player; Refresh();
                    var me0 = P(pl0.pos()); var f0 = P(pl0.check_pos);
                    Target near = null; float nd = float.MaxValue;
                    foreach (var t in _targets) if (t.Person) { float d = (t.Pos - f0).magnitude; if (d < 22 && d < nd) { nd = d; near = t; } }
                    _talk = near;
                }
                catch { }
            }
            if (string.IsNullOrEmpty(pr) || !pr.EndsWith("checked")) return;
            try
            {
                var pl = Player; if (pl == null) return;
                Refresh();
                var me = P(pl.pos()); var front = P(pl.check_pos);
                bool talk = pr.StartsWith("Talk");
                Target best = null; float bd = float.MaxValue;
                foreach (var t in _targets)
                {
                    if (t.Person != talk) continue;
                    if (!t.Person) { if (t.Areas.Exists(a => InArea(a, a.Foot ? me : front))) { best = t; break; } continue; }
                    float d = (t.Pos - front).magnitude; if (d < 22 && d < bd) { bd = d; best = t; }
                }
                if (best != null && _checked.Add(_map + ":" + best.Key)) Main.Log.Msg($"GK2 checked: {best.Key} {best.Name}");
            }
            catch (Exception e) { Main.Log.Warning("GK2 checked: " + e.Message); }
        }

        private const int Cell = 4;
        private const float WallGap = 6f;     // how close Edgeworth's middle can get to a wall line
        private const float PersonGap = 14f;  // ... and to someone standing there
        private const float Reach = 16f;      // the game checks this far in front of him

        public static bool Active
        {
            get
            {
                try
                {
                    var mc = Gk2Map.Ctrl; if (mc == null) return false;
                    var pl = mc.taskCtrl()?.getPlayer();
                    return pl != null && pl.getControl() == G2.MAP_CHAR_CONTROL.MAP_CHAR_CONTROL_PL;
                }
                catch { return false; }
            }
        }

        private static G2.cMapChar Player => Gk2Map.Ctrl?.taskCtrl()?.getPlayer();
        private static Vector2 P(G2.GKJ2Global.cVector3D v) => new Vector2(Gk2Map.Fx(v.x), Gk2Map.Fx(v.y));
        private static Vector2 P(G2.cVector2D v) => new Vector2(Gk2Map.Fx(v.x), Gk2Map.Fx(v.y));

        // ---------- reading the room ----------

        private static bool _timedOnce;
        private static System.Diagnostics.Stopwatch _rsw = new System.Diagnostics.Stopwatch();
        private static void Lap(string what) { if (!_timedOnce && _rsw.ElapsedMilliseconds > 50) Main.Log.Msg($"GK2 room data: {what} at {_rsw.ElapsedMilliseconds} ms"); }

        private static void Refresh()
        {
            _rsw.Restart();
            try { Refresh0(); } finally { if (!_timedOnce) Main.Log.Msg($"GK2 room data first read: {_rsw.ElapsedMilliseconds} ms"); _timedOnce = true; }
        }

        private static void Refresh0()
        {
            _targets.Clear(); _walls.Clear();
            var mc = Gk2Map.Ctrl; if (mc == null) return;
            string map = "";
            try { map = mc.getMapID().ToString(); } catch { }
            if (map != _map) { _map = map; _sel = null; }
            var co = mc.coll();
            var tc = mc.taskCtrl();
            G2.cMapChar pl = null, pa = null;
            try { pl = tc.getPlayer(); pa = tc.getPartner(); } catch { }

            // Walls.
            _minX = _minY = float.MaxValue; _maxX = _maxY = float.MinValue;
            try
            {
                var ad = co.getAdjustCollTbl(); int na = co.getAdjustCollNum();
                for (int i = 0; i < na && ad != null && i < ad.Length; i++)
                {
                    var a = ad[i]; if (a == null) continue;
                    if (a.RefFlagNo != 0 && !On(co, a.RefFlagNo)) continue;
                    var s = new Seg { A = P(a.Start), B = P(a.End), Gate = a.RefFlagNo != 0 };
                    _walls.Add(s);
                    foreach (var v in new[] { s.A, s.B }) { _minX = Math.Min(_minX, v.x); _minY = Math.Min(_minY, v.y); _maxX = Math.Max(_maxX, v.x); _maxY = Math.Max(_maxY, v.y); }
                }
            }
            catch (Exception e) { Main.Log.Warning("GK2 walls: " + e.Message); }
            if (_minX > _maxX) { _minX = 0; _minY = 0; _maxX = 512; _maxY = 288; }

            Lap("before Examine areas, on");
            // Examine areas, one thing per script label.
            try
            {
                var chk = co.getCheckCollTbl(); int nc = co.getCheckCollNum();
                for (int i = 0; i < nc && chk != null && i < chk.Length; i++)
                {
                    var c = chk[i]; if (c == null || !On(co, c.RefFlagNo)) continue;
                    var key = "chk" + c.ScriptLabelNo;
                    var t = _targets.Find(x => x.Key == key);
                    if (t == null) { t = new Target { Key = key, Label = c.ScriptLabelNo }; _targets.Add(t); }
                    t.Areas.Add(new Area { A = P(c.Start), B = P(c.End), Shape = c.Shape.ToString(), Foot = c.FootHit });
                }
                foreach (var t in _targets)
                {
                    var m = Vector2.zero; foreach (var a in t.Areas) m += (a.A + a.B) / 2; t.Pos = m / t.Areas.Count;
                    t.Name = NameFor(t.Key) ?? "something to examine, " + Where(t.Pos);
                }
            }
            catch (Exception e) { Main.Log.Warning("GK2 checks: " + e.Message); }

            Lap("before People (not Edgew");
            // People (not Edgeworth, not the partner who follows him).
            try
            {
                int n = 0;
                for (var task = tc.getFirst(); task != null && n < 300; task = tc.getNext(task), n++)
                {
                    var ch = task.TryCast<G2.cMapChar>();
                    if (ch == null || ch.isInvisible()) continue;
                    if (pl != null && ch.Pointer == pl.Pointer) continue;
                    if (pa != null && ch.Pointer == pa.Pointer) continue;
                    if (ch.getControl() == G2.MAP_CHAR_CONTROL.MAP_CHAR_CONTROL_PL) continue;
                    int res = (int)ch.getResID(); int nid = (int)ch.getNameID();
                    var t = new Target { Key = "chr" + res + "_" + nid, Person = true, Pos = P(ch.pos()), Res = res, NameId = nid };
                    t.Name = PersonName(ch.getResID().ToString(), res, nid);
                    _targets.Add(t);
                }
            }
            catch (Exception e) { Main.Log.Warning("GK2 people: " + e.Message); }

            Lap("before Same names: numbe");
            // Same names: number them.
            var groups = new Dictionary<string, List<Target>>();
            foreach (var t in _targets) { if (!groups.TryGetValue(t.Name, out var l)) groups[t.Name] = l = new List<Target>(); l.Add(t); }
            foreach (var l in groups.Values) if (l.Count > 1) for (int i = 0; i < l.Count; i++) l[i].Name += " " + (i + 1);

            Lap("before Nearest first.");
            // Nearest first.
            if (pl != null)
            {
                var me = P(pl.pos());
                _targets.Sort((a, b) => (a.Pos - me).sqrMagnitude.CompareTo((b.Pos - me).sqrMagnitude));
            }
        }

        private static bool On(G2.cMapColl co, uint flag) { try { return co.isCollEnable(flag); } catch { return true; } }

        private static readonly Dictionary<string, string> People = new Dictionary<string, string>
        {
            { "MITSURUGI", "Edgeworth" }, { "ITONOKO", "Gumshoe" }, { "BODYGURD", "bodyguard" }, { "KANSHIKI", "forensics officer" },
            { "MIKUMO", "Kay" }, { "KARUMAMEI", "Franziska" }, { "ROW", "Lang" }, { "BADHO", "Badd" }, { "SAIBANCHO", "judge" },
        };

        internal static int PersonNum(int res, int nid) => 900000 + res * 16 + (nid & 15);

        private static string PersonName(string resName, int res, int nid)
        {
            var learned = ObjNames.LearnedName(PersonNum(res, nid));
            if (!string.IsNullOrEmpty(learned)) return learned;
            string n = resName.StartsWith("MAPTASK_RES_ID_") ? resName.Substring(15) : resName;
            if (People.TryGetValue(n, out var p)) return p;
            n = n.Replace('_', ' ').ToLowerInvariant();
            return n.Length == 0 ? "someone" : char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

        /// <summary>Where in the room: by thirds of the walls' extent (north is up).</summary>
        private static string Where(Vector2 p)
        {
            float fx = (p.x - _minX) / Math.Max(1, _maxX - _minX), fy = (p.y - _minY) / Math.Max(1, _maxY - _minY);
            string ns = fy < 1 / 3f ? "north" : fy > 2 / 3f ? "south" : "", ew = fx < 1 / 3f ? "west" : fx > 2 / 3f ? "east" : "";
            string d = ns.Length > 0 && ew.Length > 0 ? ns + "-" + ew : ns + ew;
            return d.Length == 0 ? "middle of the room" : d;
        }

        // ---------- [ ] and ' ----------

        public static void Step(int dir)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Refresh();
                long tRefresh = sw.ElapsedMilliseconds;
                var pl = Player;
                if (_targets.Count == 0 || pl == null) { Speech.Say("Nothing to examine here.", true, false); return; }
                int cur = _targets.FindIndex(t => t.Key == _sel);
                int i = cur < 0 ? (dir > 0 ? 0 : _targets.Count - 1) : (cur + dir + _targets.Count) % _targets.Count;
                var tg = _targets[i]; _sel = tg.Key;
                var me = P(pl.pos());
                Main.Log.Msg($"GK2 select {i + 1}/{_targets.Count}: {tg.Key} {tg.Name} at {tg.Pos.x:0},{tg.Pos.y:0}; me {me.x:0},{me.y:0}");
                Speech.Say(Menus.Join(tg.Name, tg.Person ? "talk" : "examine", IsChecked(tg) ? "checked" : null, Direction(me, tg.Pos), Reachable(tg, me) || !GatedOff(tg, me) ? null : "closed off for now", $"{i + 1} of {_targets.Count}"), true, false);
                Main.Log.Msg($"GK2 step took {sw.ElapsedMilliseconds} ms (room data {tRefresh} ms)");
            }
            catch (Exception e) { Main.Log.Error("GK2 step failed: " + e); }
        }

        // ---------- tracking: a sound from the way to the chosen thing ----------
        // Like the first game: the sound comes from the next corner of the way there (left or right, higher further up
        // the room, louder closer), repeats faster as you get closer, and two notes mean you're there.
        private static bool _tracking;
        private static string _trackKey, _trackMap;
        private static float _nextPing, _trkAt;
        private static readonly List<Vector2> _trkRoute = new List<Vector2>();
        private static Vector2 _trkFrom;
        private static bool _trkOk;

        public static void Ping()
        {
            if (_tracking) { _tracking = false; Speech.Say("Tracking off.", true, false); return; }
            try
            {
                Refresh();
                var t = _targets.Find(x => x.Key == _sel); var pl = Player;
                if (t == null || pl == null) { Speech.Say("Choose something first with the bracket keys.", true, false); return; }
                _tracking = true; _trackKey = t.Key; _trackMap = _map; _nextPing = Time.unscaledTime + 1.2f; _trkAt = -10;
                Speech.Say(Menus.Join("Tracking " + t.Name, Direction(P(pl.pos()), t.Pos)), true, false);
            }
            catch (Exception e) { Main.Log.Error("GK2 ping failed: " + e.Message); }
        }

        private static void PollTracking()
        {
            if (!_tracking) return;
            float now = Time.unscaledTime;
            if (now < _nextPing) return;
            // Quiet while walking on its own, during conversations and close-ups; carry on afterwards.
            if (_walking || !Active || now - Dialogue.MessageSeenAt < 0.6f || Gk2Examine.Active || Gk2Examine3D.Active || Gk2Item2D.Active)
            { _nextPing = now + 0.3f; return; }
            if (now - Earcons.LastPromptSoundAt < 0.5f) { _nextPing = now + 0.2f; return; }
            if (_sel != null && _sel != _trackKey) { _trackKey = _sel; _trkAt = -10; }   // follow a new choice from the bracket keys
            if (_map != _trackMap) { _tracking = false; Speech.Say("Tracking off, new room.", false, false); return; }
            var t = _targets.Find(x => x.Key == _trackKey); var pl = Player;
            if (t == null || pl == null) { _tracking = false; Speech.Say("Tracking off, it's gone.", false, false); return; }
            var me = P(pl.pos());
            // Work the way out again only after moving a little.
            if ((me - _trkFrom).magnitude > 6 || now - _trkAt > 2f)
            {
                try { _trkOk = Plan(t, me, out _, false, _trkRoute); } catch { _trkOk = false; }
                _trkFrom = me; _trkAt = now;
            }
            var route = _trkOk ? _trkRoute : null;
            while (route != null && route.Count > 0 && (route[0] - me).magnitude < 5) route.RemoveAt(0);
            bool there;
            Vector2 aim; float dist;
            string pr = null; try { pr = Investigation.CurrentPrompt; } catch { }
            if (route == null) { aim = t.Pos; dist = (t.Pos - me).magnitude; there = false; }
            else if (route.Count == 0) { aim = t.Pos; dist = 0; there = true; }
            else
            {
                float len = 0; var prev = me; foreach (var pt in route) { len += (pt - prev).magnitude; prev = pt; }
                aim = route[0]; foreach (var pt in route) if ((pt - me).magnitude > 8) { aim = pt; break; }
                dist = len; there = len < 5 && !string.IsNullOrEmpty(pr);
            }
            float dx = aim.x - me.x, dy = aim.y - me.y;   // y grows southwards
            double pan = dx / 100.0;
            double freq = 523 * Math.Pow(2, Math.Max(-1, Math.Min(1, -dy / 100.0)));
            double vol = 0.25 + 0.75 * (1 - Math.Min(dist, 250) / 250);
            Earcons.PlayPositional(pan, freq, vol, there ? 2 : 1);
            float interval = there ? 1.2f : 0.35f + 0.85f * Math.Min(dist, 200) / 200;
            _nextPing = now + interval;
        }

        private static string Direction(Vector2 me, Vector2 to)
        {
            var d = to - me;
            if (d.magnitude < 12) return "right here";
            double ang = Math.Atan2(-d.y, d.x) * 180 / Math.PI;   // 0 = east, 90 = north
            string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            int k = (int)Math.Round(((ang % 360) + 360) % 360 / 45.0) % 8;
            return names[k] + (d.magnitude > 80 ? ", far" : "");
        }

        // ---------- walking ----------

        private static List<Vector2> _path;
        private static int _pathPos;
        private static bool _walking;
        private static Target _walkT;
        private static int _faceKeys;
        private static float _faceUntil, _lastProgress, _best;
        private static int _replans;
        private static readonly HashSet<int> _learnedBlock = new HashSet<int>();
        private static string _learnedMap;

        // 8 directions: vector, and the W A S D keys (1 W, 2 A, 4 S, 8 D).
        private static readonly Vector2[] DirV = { new Vector2(0, -1), new Vector2(1, -1).normalized, new Vector2(1, 0), new Vector2(1, 1).normalized, new Vector2(0, 1), new Vector2(-1, 1).normalized, new Vector2(-1, 0), new Vector2(-1, -1).normalized };
        private static readonly int[] DirK = { 1, 1 | 8, 8, 4 | 8, 4, 4 | 2, 2, 1 | 2 };

        public static void Walk()
        {
            if (_walking) { Stop("Stopped."); return; }
            try
            {
                Refresh();
                var t = _targets.Find(x => x.Key == _sel); var pl = Player;
                if (t == null || pl == null) { Speech.Say("Choose something first with the bracket keys.", true, false); return; }
                if (_learnedMap != _map) { _learnedBlock.Clear(); _learnedMap = _map; }
                var me = P(pl.pos());
                if (!Plan(t, me, out int faceDir))
                {
                    Speech.Say(GatedOff(t, me) ? t.Name + " is in a part of the area that is closed off for now." : "Can't find a way to " + t.Name + ".", true, false); return;
                }
                _walkT = t; _walking = true; _replans = 0; _faceKeys = DirK[faceDir];
                _lastProgress = Time.unscaledTime; _best = float.MaxValue;
                Speech.Say("Walking to " + t.Name, true, false);
            }
            catch (Exception e) { Main.Log.Error("GK2 walk failed: " + e); Stop(null); }
        }

        private static void Stop(string say)
        {
            _walking = false; Navigator.HoldKeys(0);
            if (say != null) Speech.Say(say, true, false);
        }

        private static int W => (int)((_maxX - _minX) / Cell) + 9;
        private static int H => (int)((_maxY - _minY) / Cell) + 9;
        private static Vector2 CellPos(int cx, int cy) => new Vector2(_minX - 16 + cx * Cell + Cell / 2f, _minY - 16 + cy * Cell + Cell / 2f);
        private static int CellX(float x) => (int)((x - (_minX - 16)) / Cell);
        private static int CellY(float y) => (int)((y - (_minY - 16)) / Cell);

        private static bool Blocked(Vector2 p, Target goal)
        {
            foreach (var s in _walls) if (!(_baseOnly && s.Gate) && DistSeg(p, s.A, s.B) < WallGap) return true;
            foreach (var t in _targets)
                if (t.Person && (t.Pos - p).magnitude < PersonGap) return true;
            return false;
        }

        private static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.sqrMagnitude;
            float t = l2 < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Can it be used from here, facing direction d? (The game checks 16 in front of him, or under his feet.)</summary>
        private static bool UsableFrom(Target t, Vector2 p, int d)
        {
            if (t.Person)
            {
                var to = t.Pos - p; float dist = to.magnitude;
                if (dist < 12 || dist > 26) return false;
                return Vector2.Dot(to.normalized, DirV[d]) > 0.9f;
            }
            var front = p + DirV[d] * Reach;
            foreach (var a in t.Areas)
                if (InArea(a, a.Foot ? p : front)) return true;
            return false;
        }

        private static bool InArea(Area a, Vector2 p)
        {
            float x0 = Math.Min(a.A.x, a.B.x), x1 = Math.Max(a.A.x, a.B.x), y0 = Math.Min(a.A.y, a.B.y), y1 = Math.Max(a.A.y, a.B.y);
            if (p.x < x0 || p.x > x1 || p.y < y0 || p.y > y1) return false;
            float u = (p.x - x0) / Math.Max(1e-3f, x1 - x0), v = (p.y - y0) / Math.Max(1e-3f, y1 - y0);
            switch (a.Shape)
            {
                case "MAP_COLL_SHAPE_TRI_TL": return u + v <= 1;
                case "MAP_COLL_SHAPE_TRI_TR": return (1 - u) + v <= 1;
                case "MAP_COLL_SHAPE_TRI_BL": return u + (1 - v) <= 1;
                case "MAP_COLL_SHAPE_TRI_BR": return (1 - u) + (1 - v) <= 1;
                case "MAP_COLL_SHAPE_DIAMOND": return Math.Abs(u - 0.5f) + Math.Abs(v - 0.5f) <= 0.5f;
                default: return true;
            }
        }

        /// <summary>No way there, but there would be if the story's barriers were open: the area is closed off for now.</summary>
        private static bool GatedOff(Target t, Vector2 me) => Cached("g", t, me, () =>
        {
            _baseOnly = true;
            try { return Plan(t, me, out _, true); } finally { _baseOnly = false; }
        });

        private static readonly Dictionary<string, bool> _reachCache = new Dictionary<string, bool>();
        private static string _reachSig = "";

        private static bool Cached(string kind, Target t, Vector2 me, Func<bool> f)
        {
            string sig = _map + "|" + _walls.Count + "|" + CellX(me.x) + "," + CellY(me.y);
            if (sig != _reachSig) { _reachSig = sig; _reachCache.Clear(); }
            string k = kind + t.Key;
            if (_reachCache.TryGetValue(k, out var v)) return v;
            v = f(); _reachCache[k] = v; return v;
        }

        private static bool Reachable(Target t, Vector2 me) => Cached("r", t, me, () => { try { return Plan(t, me, out _, true); } catch { return true; } });

        private static bool Plan(Target t, Vector2 from, out int faceDir, bool quiet = false, List<Vector2> into = null)
        {
            faceDir = 0;
            int w = W, h = H;
            var prev = new int[w * h];
            for (int i = 0; i < prev.Length; i++) prev[i] = -2;
            int sx = Mathf.Clamp(CellX(from.x), 0, w - 1), sy = Mathf.Clamp(CellY(from.y), 0, h - 1);
            var q = new Queue<int>(); q.Enqueue(sy * w + sx); prev[sy * w + sx] = -1;
            int goal = -1;
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            // Each cell's "blocked" test is worked out once.
            var blk = new sbyte[w * h];
            bool IsBlk(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return true;
                int i = y * w + x;
                if (blk[i] == 0) blk[i] = (sbyte)(Blocked(CellPos(x, y), t) ? 1 : 2);
                return blk[i] == 1;
            }
            while (q.Count > 0 && goal < 0)
            {
                int c = q.Dequeue(); int cx = c % w, cy = c / w;
                var p = CellPos(cx, cy);
                for (int d = 0; d < 8; d++) if (UsableFrom(t, p, d)) { goal = c; faceDir = d; break; }
                if (goal >= 0) break;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + dx[k], ny = cy + dy[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (prev[n] != -2) continue;
                    if (_learnedBlock.Contains(n) || IsBlk(nx, ny)) continue;
                    if (k >= 4 && (IsBlk(cx + dx[k], cy) || IsBlk(cx, cy + dy[k]))) continue;
                    prev[n] = c; q.Enqueue(n);
                }
            }
            if (goal < 0) { if (!quiet && into == null) Main.Log.Msg($"GK2 no path to {t.Key} from {from.x:0},{from.y:0}"); return false; }
            if (quiet) return true;
            var cells = new List<int>();
            for (int c = goal; c >= 0; c = prev[c]) cells.Add(c);
            cells.Reverse();
            // Keep only the corners.
            var path = into ?? new List<Vector2>(); path.Clear();
            for (int i = 1; i < cells.Count; i++)
            {
                bool last = i == cells.Count - 1;
                if (!last)
                {
                    int a = cells[i - 1], b = cells[i], c2 = cells[i + 1];
                    if (b - a == c2 - b) continue;
                }
                path.Add(CellPos(cells[i] % w, cells[i] / w));
            }
            if (into != null) return true;
            _path = path;
            _pathPos = 0;
            Main.Log.Msg($"GK2 path to {t.Key} ({t.Name}): {string.Join(" ", _path.ConvertAll(v => $"{v.x:0},{v.y:0}"))}; face {faceDir}");
            return true;
        }

        private static string _warmMap = null;

        public static void Poll()
        {
            // Read a new room as soon as it's there, so the first [ or ] press doesn't wait for it.
            if (Active)
            {
                string m = null; try { m = Gk2Map.Ctrl?.getMapID().ToString(); } catch { }
                if (m != null && m != _warmMap) { _warmMap = m; try { Refresh(); } catch { } }
            }
            if (Active) TrackChecked();
            try { PollTracking(); } catch (Exception e) { Main.Log.Warning("GK2 tracking: " + e.Message); _tracking = false; }
            if (!_walking) return;
            if (!Active || !Main.GameHasFocus) { Stop(null); return; }
            try
            {
                var pl = Player; if (pl == null) { Stop(null); return; }
                var me = P(pl.pos());
                float now = Time.unscaledTime;
                if (_faceUntil > 0)
                {
                    if (now >= _faceUntil)
                    {
                        Navigator.HoldKeys(0);
                        string pr = Investigation.CurrentPrompt;
                        // The prompt can blink off for a moment as he turns: give it a little longer before saying it's missing.
                        if (string.IsNullOrEmpty(pr) && now < _faceUntil + 0.8f) return;
                        _faceUntil = 0; _walking = false;
                        Main.Log.Msg($"GK2 arrived at {me.x:0},{me.y:0} facing; prompt '{pr}'");
                        if (string.IsNullOrEmpty(pr)) Speech.Say("Here, but no prompt came up. Try turning.", false, false);
                    }
                    return;
                }
                if (_path == null || _pathPos >= _path.Count)
                {
                    // Turn to face it: a short press the right way.
                    Navigator.HoldKeys(_faceKeys); _faceUntil = now + 0.12f; return;
                }
                var target = _path[_pathPos];
                var to = target - me;
                if (to.magnitude < 3f) { _pathPos++; _best = float.MaxValue; return; }
                float rem = to.magnitude + (_path.Count - _pathPos) * 0.01f;
                if (rem < _best - 0.5f) { _best = rem; _lastProgress = now; }
                if (now - _lastProgress > 1.2f)
                {
                    // Something unseen is in the way: remember the cells just ahead and plan again.
                    var dirv = to.normalized;
                    for (float k = 4; k <= 12; k += 4)
                    {
                        var b = me + dirv * k; int bx = CellX(b.x), by = CellY(b.y);
                        for (int ox = -1; ox <= 1; ox++) for (int oy = -1; oy <= 1; oy++) _learnedBlock.Add((by + oy) * W + bx + ox);
                    }
                    Navigator.HoldKeys(0);
                    if (++_replans > 6 || !Plan(_walkT, me, out int fd)) { Stop("Stuck. Couldn't get to " + _walkT.Name + "."); return; }
                    _faceKeys = DirK[fd]; _lastProgress = now; _best = float.MaxValue;
                    return;
                }
                // Steer: the closest of the 8 directions.
                var n = to.normalized; int bestD = 0; float bd = -2;
                for (int d = 0; d < 8; d++) { float dot = Vector2.Dot(n, DirV[d]); if (dot > bd) { bd = dot; bestD = d; } }
                Navigator.HoldKeys(DirK[bestD]);
            }
            catch (Exception e) { Main.Log.Error("GK2 walk poll: " + e.Message); Stop(null); }
        }
    }
}
