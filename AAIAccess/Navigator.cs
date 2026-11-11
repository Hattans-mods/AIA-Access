extern alias GK1;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>
    /// Investigation (first game): a list of the people, things and exits in the room.
    /// [ and ] pick one (nearest first); backslash walks Edgeworth there by holding the walk keys for you,
    /// finding a way around furniture.
    /// Map positions are 16.16 fixed point; areas (flg = where you can check it from, hit = solid part)
    /// are corner offsets in pixels from the object's position.
    /// </summary>
    internal static class Navigator
    {
        private class Target
        {
            public int Num;
            public double X, Y;               // pixels
            public Vector2[] Flg, Hit;        // corners in pixels (lu, ru, rd, ld)
            public bool Person, Exit;
            public string Name;
            public uint CallA;
            public bool Off;                  // the game has switched its examine check off for now (dsp bit 0x10)
        }

        private const double StepSize = 10;   // pixels per "step" when telling distances
        private const int Cell = 4;           // path-finding grid, pixels

        private static readonly List<Target> _targets = new List<Target>();
        private static readonly List<Vector2[]> _solids = new List<Vector2[]>();
        private static int _selNum = -1;
        private static uint _map = uint.MaxValue;
        private static int _mapW = 304, _mapH = 192;
        private static Dictionary<string, bool> _checkedStore;
        private static string CheckedFile => System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_checked.txt");
        /// <summary>Checked states seen on prompts, kept between play sessions.</summary>
        private static Dictionary<string, bool> _checked
        {
            get
            {
                if (_checkedStore != null) return _checkedStore;
                _checkedStore = new Dictionary<string, bool>();
                try
                {
                    if (System.IO.File.Exists(CheckedFile))
                        foreach (var l in System.IO.File.ReadAllLines(CheckedFile))
                        {
                            int eq = l.LastIndexOf('=');
                            if (eq > 0) _checkedStore[l.Substring(0, eq)] = l.Substring(eq + 1) == "1";
                        }
                }
                catch (Exception e) { Main.Log.Warning("Couldn't read checked states: " + e.Message); }
                return _checkedStore;
            }
        }

        private static void SaveChecked()
        {
            try
            {
                var lines = new List<string>();
                foreach (var kv in _checked) lines.Add(kv.Key + "=" + (kv.Value ? "1" : "0"));
                System.IO.File.WriteAllLines(CheckedFile, lines);
            }
            catch (Exception e) { Main.Log.Warning("Couldn't save checked states: " + e.Message); }
        }
        private static string _lastPromptSeen = "";

        // Learning people's real names: after a "Talk" prompt, the first new speaker who isn't
        // Edgeworth or the partner is the person we're talking to.
        private static int _talkNum = -1;
        private static float _talkAt;
        private static string _talkSpeakerBefore = "";

        // Who spoke in the current conversation (lines per speaker), and in which things' conversations each
        // name has been heard: someone heard with several different things is a companion (e.g. the flight
        // attendant following you around), not the person you're talking to.
        private static readonly Dictionary<string, int> _convSpeakers = new Dictionary<string, int>();
        private static readonly List<string> _convOrder = new List<string>();   // speakers in the order they first spoke
        private static readonly Dictionary<string, HashSet<int>> _heardWith = new Dictionary<string, HashSet<int>>();

        /// <summary>The dialogue reader saw a new speaker's line.</summary>
        public static void HeardSpeaker(string name)
        {
            if (_usingTarget == null || string.IsNullOrEmpty(name)) return;
            if (!_convSpeakers.ContainsKey(name)) _convOrder.Add(name);
            _convSpeakers[name] = (_convSpeakers.TryGetValue(name, out int n) ? n : 0) + 1;
            if (!_heardWith.TryGetValue(name, out var set)) _heardWith[name] = set = new HashSet<int>();
            set.Add(_usingTarget.Num);
        }

        private static bool Companion(string name) => _heardWith.TryGetValue(name, out var s) && s.Count >= 2;

        /// <summary>A conversation with a person is over: their name is whoever (not Edgeworth, not a companion) spoke most.</summary>
        /// <summary>Someone else in the room already goes by this name (e.g. the dog Missile, standing nearby).</summary>
        private static bool NameElsewhere(Target t, string sp)
        {
            return _targets.Exists(o => !Same(o, t) && o.Person && !o.Exit
                && string.Equals(System.Text.RegularExpressions.Regex.Replace(o.Name, @" \d+$", ""), sp, StringComparison.OrdinalIgnoreCase));
        }

        private static void LearnFromConversation(Target t)
        {
            // A big scene where lots of people talk (more than four besides Edgeworth): no telling who this one is.
            int others = _convSpeakers.Keys.Count(k => k != "Edgeworth" && k != _partnerName && !k.Contains("?"));
            if (others > 4)
            {
                Main.Log.Msg($"Conversation with {t.Num} {ObjNames.Raw(t.Num)}: {others} people spoke ({string.Join(", ", _convSpeakers.Keys)}); too many to tell who it is");
                return;
            }
            // "???" spoke: someone not introduced yet. If a new name speaks after that, it's them being introduced
            // ("???: ..." then "Meekins: Mike Meekins, reporting for duty"); otherwise keep the game's own name.
            // Already named, and that person spoke here: keep the name, whatever else happened in the scene.
            string had = ObjNames.LearnedName(t.Num);
            if (had != null && _convSpeakers.ContainsKey(had))
            {
                Main.Log.Msg($"Conversation with {t.Num} {ObjNames.Raw(t.Num)}: {had} spoke; keeping that name");
                return;
            }
            int unknownAt = _convOrder.FindIndex(n => n.Contains("?"));
            if (unknownAt >= 0)
            {
                string revealed = null;
                for (int i = unknownAt + 1; i < _convOrder.Count && revealed == null; i++)
                {
                    string sp = _convOrder[i];
                    if (sp == "Edgeworth" || sp == _partnerName || sp.StartsWith("Gumshoe") || sp.Contains("?") || ObjNames.IsMainCharacterName(sp) || Companion(sp) || NameElsewhere(t, sp)) continue;
                    revealed = sp;
                }
                Main.Log.Msg($"Conversation with {t.Num} {ObjNames.Raw(t.Num)}: speakers {string.Join(", ", _convOrder)}; unnamed speaker, then {revealed ?? "nobody new"}");
                if (revealed == null) { ObjNames.Forget(t.Num); return; }
                if (ObjNames.KnownName(t.Num)) { Main.Log.Msg("Already known by name; not learning"); return; }
                ObjNames.Learn(t.Num, revealed);
                return;
            }
            string best = null; int bestN = 0; var companions = new List<string>();
            foreach (var kv in _convSpeakers)
            {
                string sp = kv.Key;
                if (sp == "Edgeworth" || sp == _partnerName || sp.StartsWith("Gumshoe") || sp.Contains("?")) continue;
                if (ObjNames.IsMainCharacterName(sp)) continue;   // e.g. Franziska joining in someone else's conversation
                if (NameElsewhere(t, sp)) continue;               // e.g. the dog barking in someone else's conversation
                if (Companion(sp)) { companions.Add(sp); continue; }
                if (kv.Value > bestN) { best = sp; bestN = kv.Value; }
            }
            // Only a companion spoke: then it's probably them.
            if (best == null && companions.Count == 1) best = companions[0];
            // Only main characters spoke (e.g. Kay, whose sprite here has a code name like 02_INIT): it's the one
            // of them who isn't already standing somewhere else in the room under their own name.
            if (best == null)
            {
                var mains = new List<string>();
                foreach (var kv in _convSpeakers)
                {
                    string sp = kv.Key;
                    if (sp == "Edgeworth" || sp == _partnerName || sp.StartsWith("Gumshoe") || sp.Contains("?") || !ObjNames.IsMainCharacterName(sp)) continue;
                    bool elsewhere = _targets.Exists(o => !Same(o, t) && o.Person && string.Equals(o.Name, sp, StringComparison.OrdinalIgnoreCase));
                    if (!elsewhere) mains.Add(sp);
                }
                if (mains.Count == 1) best = mains[0];
            }
            Main.Log.Msg($"Conversation with {t.Num} {ObjNames.Raw(t.Num)}: speakers {string.Join(", ", _convSpeakers.Keys)}; name {best ?? "(none)"}");
            // Already named, and that person spoke here: keep the name (others may have talked more).
            string have = ObjNames.LearnedName(t.Num);
            if (have != null && _convSpeakers.ContainsKey(have)) best = null;
            if (best != null && ObjNames.KnownName(t.Num)) { Main.Log.Msg("Already known by name; not learning"); best = null; }
            if (best != null) ObjNames.Learn(t.Num, best);
        }

        private static void LearnName()
        {
            return;
#pragma warning disable CS0162
            if (_talkNum < 0) return;
            if (Time.unscaledTime - _talkAt > 30f) { _talkNum = -1; return; }
            string sp = Dialogue.LastSpeaker;
            if (string.IsNullOrEmpty(sp) || sp == _talkSpeakerBefore || Dialogue.LastSpeakerAt < _talkAt) return;
            if (sp == "Edgeworth" || sp == _partnerName || sp.StartsWith("Gumshoe") || sp.Contains("?")) { _talkSpeakerBefore = sp; return; }
            ObjNames.Learn(_talkNum, sp);
            _talkNum = -1;
#pragma warning restore CS0162
        }
        private static int _walkedTo = -1;
        private static float _walkedAt;

        private static GK1::Il2Cpp.GKJ1 Game { get { try { return GK1::Il2Cpp.AppCore.gkj1; } catch { return null; } } }

        private static uint CurrentMap(GK1::Il2Cpp.GKJ1 g)
        {
            try { var r = g.GetRecordData(); if (r?.mapinf_data != null) return r.mapinf_data.mymap * 1000 + r.mapinf_data.myarea; } catch { }
            return 0;
        }

        // The thing's current conversation is part of the key: when the story moves on and it gets
        // something new to say, it counts as not checked again.
        private static string Key(uint map, int num, uint callA) => map + ":" + num + ":" + callA + FlagSig(callA);

        // Some things' scripts choose what to show by a story flag (e.g. the office door). Those flags'
        // current values are part of the key, so when the story moves on the thing counts as new.
        private const uint GetSystemFlagFunc = 0x40001040;
        private static readonly Dictionary<uint, uint[]> _scriptFlags = new Dictionary<uint, uint[]>();

        private static string FlagSig(uint callA)
        {
            if (callA == 0) return "";
            try
            {
                var g = Game;
                if (!_scriptFlags.TryGetValue(callA, out var flags))
                {
                    var list = new List<uint>();
                    UInt32[] dm = null;
                    try { dm = g.GetDM((int)callA); } catch { }
                    if (dm != null)
                        for (int i = 0; i + 1 < dm.Length; i++)
                            if (dm[i] == GetSystemFlagFunc && !list.Contains(dm[i + 1])) list.Add(dm[i + 1]);
                    flags = list.ToArray();
                    _scriptFlags[callA] = flags;
                }
                if (flags.Length == 0) return "";
                var sb = new System.Text.StringBuilder();
                foreach (var f in flags) sb.Append($":f{f:X}={(g.GetSystemFlag(f) ? 1 : 0)}");
                return sb.ToString();
            }
            catch { return ""; }
        }
        private static double Px(int fixedPos) => fixedPos / 65536.0;

        private static Vector2[] Quad(GK1::Il2Cpp.OBJ_ATTRI a, double x, double y)
        {
            if (a == null) return null;
            return new[]
            {
                new Vector2((float)(x + a.lux), (float)(y + a.luy)), new Vector2((float)(x + a.rux), (float)(y + a.ruy)),
                new Vector2((float)(x + a.rdx), (float)(y + a.rdy)), new Vector2((float)(x + a.ldx), (float)(y + a.ldy)),
            };
        }

        /// <summary>The thing the game's Examine / Talk prompt is for right now (its icon data), or -1.</summary>
        internal static int GamePromptNum(GK1::Il2Cpp.GKJ1 g)
        {
            try
            {
                var ic = g.IconData;
                if (ic == null || !ic.sts) return -1;
                int n = ic.IconNum;
                // Sanity check: it must be something near Edgeworth, else don't trust it.
                var t = _targets.Find(c => !c.Exit && c.Num == n);
                var me = g.GetMyCharBuff();
                if (t == null || me == null) return -1;
                double px = Px(me.xof), py = Px(me.yof);
                for (int ox = -24; ox <= 24; ox += 8)
                    for (int oy = -16; oy <= 16; oy += 8)
                        if (InQuad(t.Flg, px + ox, py + oy)) return n;
                return -1;
            }
            catch { return -1; }
        }

        // The target list is rebuilt often, so compare things by what they are, not by object.
        private static bool Same(Target a, Target b) => a != null && b != null && a.Num == b.Num && a.Exit == b.Exit;

        private static bool Empty(Vector2[] q)
        {
            if (q == null) return true;
            foreach (var p in q) if (p != q[0]) return false;
            return true;
        }

        /// <summary>Point inside a convex quad (either winding).</summary>
        private static bool InQuad(Vector2[] q, double x, double y)
        {
            if (Empty(q)) return false;
            int sign = 0;
            for (int i = 0; i < 4; i++)
            {
                var a = q[i]; var b = q[(i + 1) % 4];
                double c = (b.x - a.x) * (y - a.y) - (b.y - a.y) * (x - a.x);
                if (Math.Abs(c) < 1e-6) continue;
                int s = c > 0 ? 1 : -1;
                if (sign == 0) sign = s; else if (s != sign) return false;
            }
            return true;
        }

        /// <summary>"exit north-west": where the exit is in the room (north is up), by thirds of the floor.</summary>
        private static string ExitName(double x, double y, Vector2[] flg, Vector2[] hit)
        {
            // Its doorway: the middle of its entry area, else its position.
            var q = !Empty(hit) ? hit : !Empty(flg) ? flg : null;
            if (q != null) { x = (q[0].x + q[1].x + q[2].x + q[3].x) / 4; y = (q[0].y + q[1].y + q[2].y + q[3].y) / 4; }
            double x0 = 0, x1 = _mapW, y0 = 0, y1 = _mapH;
            if (_wall != null)
            {
                int w = _wall.GetLength(0), h = _wall.GetLength(1);
                int minX = w, maxX = -1, minY = h, maxY = -1;
                for (int cx = 0; cx < w; cx++) for (int cy = 0; cy < h; cy++)
                    if (!_wall[cx, cy]) { if (cx < minX) minX = cx; if (cx > maxX) maxX = cx; if (cy < minY) minY = cy; if (cy > maxY) maxY = cy; }
                if (maxX > minX && maxY > minY) { x0 = minX * Cell; x1 = (maxX + 1) * Cell; y0 = minY * Cell; y1 = (maxY + 1) * Cell; }
            }
            double fx = (x - x0) / Math.Max(1, x1 - x0), fy = (y - y0) / Math.Max(1, y1 - y0);
            string ns = fy < 1 / 3.0 ? "north" : fy > 2 / 3.0 ? "south" : "";
            string ew = fx < 1 / 3.0 ? "west" : fx > 2 / 3.0 ? "east" : "";
            string dir = ns.Length > 0 && ew.Length > 0 ? ns + "-" + ew : ns + ew;
            return dir.Length > 0 ? "exit " + dir : "exit, middle of the room";
        }

        private static int _scenarioLogged = -1;
        private static readonly HashSet<string> _hiddenLogged = new HashSet<string>();

        private static void Refresh(GK1::Il2Cpp.GKJ1 g)
        {
            _targets.Clear(); _solids.Clear();
            try
            {
                int sc = g.GetScenarioNum();
                if (sc != _scenarioLogged) { _scenarioLogged = sc; Main.Log.Msg($"Nav: scenario {sc}"); }
                // Episode 4 (scenario 3) is the flashback: the game's OLD_ people are the young versions there.
                ObjNames.Flashback = sc == 3;
            }
            catch { }
            try { var mb = g.GetRecordData()?.mapbuff_data; if (mb != null && mb.map_sx > 0) { _mapW = mb.map_sx; _mapH = mb.map_sy; } } catch { }
            var me = g.GetMyCharBuff();
            var objs = g.GetMapObjData();
            if (objs == null) return;
            // The walls (for where the floor is) are needed to tell where the exits are.
            if (me != null) { try { LoadWalls(g, CurrentMap(g), Px(me.xof), Px(me.yof)); } catch { } }
            for (int i = 0; i < objs.Length; i++)
            {
                var o = objs[i];
                if (o == null || o.sts == 0) continue;
                if (me != null && (o.Pointer == me.Pointer || o.num == me.num)) continue;
                double x = Px(o.xof), y = Px(o.yof);
                bool inside = x >= 0 && y >= 0 && x <= _mapW && y <= _mapH;
                var hit = Quad(o.hit, x, y);
                var flg = Quad(o.flg, x, y);
                // Exits, and "zones" that work like exits (walking in takes you on).
                bool exit = o.ctrltype == 3 || ObjNames.Raw(o.num).Contains("ZONE");
                // People and things parked at 0,0 aren't really in this room (the game hides them there).
                if (!exit && x == 0 && y == 0) continue;
                // Not shown right now (display bit off): someone waiting off-stage, or the other version of a thing
                // that has two (e.g. OVAL_FIREPLACE / OVAL_FIREPLACE2). The game parks such people out of reach.
                if (!exit && (o.dsp & 1) == 0)
                {
                    if (_hiddenLogged.Add(CurrentMap(g) + ":" + o.num)) Main.Log.Msg($"Nav: {o.num} {ObjNames.Raw(o.num)} isn't shown (dsp={o.dsp:X}); leaving it out");
                    continue;
                }
                bool wholeMap = flg != null && Math.Abs(o.flg.rux - o.flg.lux) >= _mapW - 4 && Math.Abs(o.flg.ldy - o.flg.luy) >= _mapH - 4;
                // Solid furniture and people (exits and whole-room zones are not solid).
                if (inside && !exit && !wholeMap && !Empty(hit)) _solids.Add(hit);
                if (!inside || wholeMap || Empty(flg)) continue;
                if (!exit && o.call_a == 0 && o.call_x == 0)
                {
                    if (ObjNames.IsPerson(o.num)) _partnerName = ObjNames.Name(o.num);
                    continue;   // nothing happens there (or your partner)
                }
                // No Examine / Talk icon right now: the game won't let you use it at the moment
                // (it still blocks the way, so it stays a wall above).
                if (!exit && o.iconnum == 0) continue;
                _targets.Add(new Target
                {
                    Num = o.num, X = x, Y = y, Flg = flg, Hit = hit, Exit = exit, CallA = o.call_a,
                    // The game's own icon says whether it's someone to talk to (2) or something to examine (1).
                    Person = !exit && (o.iconnum == 2 || (o.iconnum != 1 && ObjNames.IsPerson(o.num))),
                    Name = exit ? ExitName(x, y, flg, hit) : ObjNames.Name(o.num),
                    Off = !exit && (o.dsp & 0x10) == 0,
                });
            }
            // Someone sitting on a chair: the game puts their Talk on the chair, and the person themselves can't be
            // reached. One entry, with the person's name and the chair's place.
            foreach (var o in _targets.ToArray())
            {
                if (!o.Person || o.Exit || ObjNames.IsPerson(o.Num)) continue;
                var oc = HitCentre(o);
                var pr = _targets.Find(q => q != o && q.Person && !q.Exit && ObjNames.IsPerson(q.Num) && (HitCentre(q) - oc).magnitude < 40);
                if (pr == null) continue;
                o.Name = pr.Name;
                _targets.Remove(pr);
            }
            // Several things or people with the same name (e.g. three "mob" people, two plants): number them all,
            // in the game's own order (MOB1, MOB2, MOB3).
            foreach (var grp in _targets.FindAll(o => !o.Exit).GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
            {
                var list = grp.OrderBy(o => ObjNames.Raw(o.Num), StringComparer.Ordinal).ToList();
                if (list.Count < 2) continue;
                for (int i = 0; i < list.Count; i++) list[i].Name = list[i].Name + " " + (i + 1);
            }
            if (me != null)
            {
                double px = Px(me.xof), py = Px(me.yof);
                _targets.Sort((a, b) => a.Exit != b.Exit ? a.Exit.CompareTo(b.Exit) : GoalDist(a, px, py).CompareTo(GoalDist(b, px, py)));
            }
        }

        /// <summary>Distance to the nearest point you can check the thing from.</summary>
        private static double GoalDist(Target t, double px, double py)
        {
            var p = NearestPoint(t, px, py);
            return Math.Sqrt((p.x - px) * (p.x - px) + (p.y - py) * (p.y - py));
        }

        private static Vector2 NearestPoint(Target t, double px, double py)
        {
            if (InQuad(t.Flg, px, py)) return new Vector2((float)px, (float)py);
            float minX = Mathf.Min(Mathf.Min(t.Flg[0].x, t.Flg[1].x), Mathf.Min(t.Flg[2].x, t.Flg[3].x));
            float maxX = Mathf.Max(Mathf.Max(t.Flg[0].x, t.Flg[1].x), Mathf.Max(t.Flg[2].x, t.Flg[3].x));
            float minY = Mathf.Min(Mathf.Min(t.Flg[0].y, t.Flg[1].y), Mathf.Min(t.Flg[2].y, t.Flg[3].y));
            float maxY = Mathf.Max(Mathf.Max(t.Flg[0].y, t.Flg[1].y), Mathf.Max(t.Flg[2].y, t.Flg[3].y));
            Vector2 best = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
            double bd = double.MaxValue;
            for (float y = minY; y <= maxY; y += 2)
                for (float x = minX; x <= maxX; x += 2)
                {
                    if (!InQuad(t.Flg, x, y)) continue;
                    double d = (x - px) * (x - px) + (y - py) * (y - py);
                    if (d < bd) { bd = d; best = new Vector2(x, y); }
                }
            return best;
        }

        // Step counts and directions aren't read for now (the player asked); set true to bring them back.
        private const bool ReadSteps = false;
        private static string Spoken(Target t, double px, double py) =>
            ReadSteps ? Where(t, px, py) : (InQuad(t.Flg, px, py) ? "right here" : "");

        private static string Where(Target t, double px, double py)
        {
            if (InQuad(t.Flg, px, py)) return "right here";
            // Along the way the walk would go, around walls and things.
            var route = Route(t, px, py);
            if (route != null && route.Count > 0) return Legs(route, px, py, out _);
            var p = NearestPoint(t, px, py);
            double dx = p.x - px, dy = p.y - py;
            int sx = (int)Math.Round(Math.Abs(dx) / StepSize), sy = (int)Math.Round(Math.Abs(dy) / StepSize);
            var parts = new List<string>();
            if (sx > 0) parts.Add($"{sx} {(dx < 0 ? "left" : "right")}");
            if (sy > 0) parts.Add($"{sy} {(dy < 0 ? "up" : "down")}");
            return parts.Count == 0 ? "right here" : string.Join(", ", parts);
        }

        private static string Describe(Target t, double px, double py, uint map)
        {
            string kind = t.Exit ? "" : t.Person ? "talk" : "examine";
            bool done = IsChecked(t, map);
            return Menus.Join(t.Name, kind, done ? "checked" : "", t.Off ? "can't be used right now" : "", Spoken(t, px, py));
        }

        // Checked state: what we saw on the game's prompt, else the game's own "already read" flag for the
        // thing's conversation, once that flag has agreed with what the prompts showed.
        private static Target _pendTarget;
        private static Vector2 _pendPos;
        // The thing the prompt is for right now (when sure), and the thing being examined / talked to.
        private static Target _promptTarget, _usingTarget;
        // Where its prompt (and not a neighbour's) was seen for each thing: the best place to walk to.
        private static readonly Dictionary<string, Vector2> _goodSpot = new Dictionary<string, Vector2>();
        private static string SpotKey(uint map, int num) => map + ":" + num;
        // End points that brought up a neighbour's prompt instead, per thing: avoided next time.
        private static readonly Dictionary<string, List<Vector2>> _badGoal = new Dictionary<string, List<Vector2>>();
        private static Target _verify; private static float _verifyAt; private static int _verifyTries; private static Vector2 _verifyGoal;
        /// <summary>The name of the thing a prompt of this kind is for, where Edgeworth stands; "" when unsure.</summary>
        public static string OwnerName(bool talk)
        {
            try
            {
                var g = Game; if (g == null) return "";
                var me = g.GetMyCharBuff(); if (me == null) return "";
                Refresh(g);
                double px = Px(me.xof), py = Px(me.yof);
                Target here = null; int n = 0; double hd = double.MaxValue;
                foreach (var c in _targets)
                {
                    if (c.Exit || c.Person != talk || !InQuad(c.Flg, px, py)) continue;
                    n++;
                    var hc = HitCentre(c);
                    double dd = (hc.x - px) * (hc.x - px) + (hc.y - py) * (hc.y - py);
                    if (_walkedTo == c.Num || (_walking && Same(_walkTarget, c))) dd = -1;
                    if (dd < hd) { hd = dd; here = c; }
                }
                bool sure = here != null && (n == 1 || hd < 0);
                if (!sure && n == 0 && _walking && _walkTarget != null && !_walkTarget.Exit && _walkTarget.Person == talk) return "";
                return sure ? here.Name : "";
            }
            catch { return ""; }
        }

        /// <summary>The prompt showing now is for a different kind of thing (Talk vs Examine) than t.</summary>
        private static bool PromptIsOther(Target t)
        {
            string p = Investigation.CurrentPrompt;
            if (string.IsNullOrEmpty(p)) return false;
            // The game says which thing its prompt is for: anything else's prompt is "other".
            if (!t.Exit) { var g = Game; int gp = g == null ? -1 : GamePromptNum(g); if (gp >= 0) return gp != t.Num; }
            return p.StartsWith("Talk") != t.Person;
        }
        private static string _promptKey, _usingKey;
        private static float _usingSince;
        private static bool _couldWalk;
        private static string _pendPrompt = "";
        private static uint _pendMap;
        private static float _pendAt;
        private static string _partnerName = "";

        // Several ways the game might store "already seen" for a thing. Each prompt the mod is sure
        // about scores them; the best one (right every time so far, at least 3 times) is then used.
        private const int Formulas = 6;
        private static readonly int[] _fAgree = new int[Formulas], _fDisagree = new int[Formulas];

        private static int Flag(uint id)
        {
            if (id == 0) return -1;
            try { var g = Game; return g == null ? -1 : (g.GetReadMsgFlag(id) ? 1 : 0); } catch { return -1; }
        }

        /// <summary>The map's call table entry for a thing: (call, demo), or zeros.</summary>
        /// <summary>The game's "read in this playthrough" flag (the other one counts any playthrough).</summary>
        private static int PlayFlag(uint id)
        {
            if (id == 0) return -1;
            try { var g = Game; return g == null ? -1 : (g.GetPlayReadMsgFlag(id) ? 1 : 0); } catch { return -1; }
        }

        private static (uint call, uint demo) CallEntry(Target t, bool byNum)
        {
            try
            {
                var tbl = Game?.GetRecordData()?.mapbuff_data?.call_tbl;
                if (tbl == null) return (0, 0);
                for (int i = 0; i < tbl.Length; i++)
                {
                    var e = tbl[i]; if (e == null) continue;
                    if (byNum ? e.num == t.Num : (uint)e.call == t.CallA) return ((uint)e.call, e.demo);
                }
            }
            catch { }
            return (0, 0);
        }

        private static int ReadFormula(int f, Target t)
        {
            switch (f)
            {
                case 0: return Flag(t.CallA);
                case 1: return Flag(CallEntry(t, false).demo);
                case 2: return Flag(CallEntry(t, true).demo);
                case 3: { var e = CallEntry(t, true); return Flag(e.call); }
                case 4: return PlayFlag(t.CallA);
                case 5: return PlayFlag(CallEntry(t, true).demo);
            }
            return -1;
        }

        private static int BestFormula()
        {
            int best = -1, bestAgree = 2;
            for (int f = 0; f < Formulas; f++)
                if (_fDisagree[f] == 0 && _fAgree[f] > bestAgree) { best = f; bestAgree = _fAgree[f]; }
            return best;
        }

        private static void Score(Target t, bool done)
        {
            var sb = new System.Text.StringBuilder();
            for (int f = 0; f < Formulas; f++)
            {
                int v = ReadFormula(f, t);
                if (v >= 0) { if ((v == 1) == done) _fAgree[f]++; else _fDisagree[f]++; }
                sb.Append($" f{f}={v}({_fAgree[f]}/{_fDisagree[f]})");
            }
            var en = CallEntry(t, true);
            Main.Log.Msg($"Checked {done} for {t.Num} callA={t.CallA:X} tbl(call={en.call:X} demo={en.demo:X}):{sb}");
        }

        private static bool IsChecked(Target t, uint map)
        {
            // The game's own record (once a way of reading it has proven right) updates by itself;
            // otherwise what the prompt showed last time.
            int bf = BestFormula();
            if (bf >= 0)
            {
                int v = ReadFormula(bf, t);
                if (v >= 0) return v == 1;
            }
            if (_checked.TryGetValue(Key(map, t.Num, t.CallA), out bool c)) return c;
            return false;
        }

        /// <summary>The game set the prompt's check mark (Harmony hook): the most direct truth there is.</summary>
        public static void OnReadIcon(bool isRead) { _hookRead = isRead; _hookAt = Time.unscaledTime; }
        private static bool _hookRead;
        private static float _hookAt = -10f;

        // Does examining this thing lead to a deduction? The game's script for it is searched for the
        // "start deduction" commands (INFER_INI / FUNC_INQUIRY_INFER).
        private const uint InferIni = 1073741895, InquiryInfer = 1073746108;
        private static readonly Dictionary<uint, bool> _deductionCache = new Dictionary<uint, bool>();

        private static bool HasDeduction(Target t)
        {
            if (t.CallA == 0 || t.Exit) return false;
            if (_deductionCache.TryGetValue(t.CallA, out bool known)) return known;
            bool found = false; int len = -1;
            try
            {
                var g = Game;
                // Search the thing's script, and the scripts it refers to (two levels down).
                var seen = new HashSet<uint>();
                var queue = new Queue<(uint id, int depth)>();
                queue.Enqueue((t.CallA, 0));
                while (queue.Count > 0 && !found && seen.Count < 40)
                {
                    var (id, depth) = queue.Dequeue();
                    if (!seen.Add(id)) continue;
                    UInt32[] dm = null;
                    try { dm = g.GetDM((int)id); } catch { }
                    if (dm == null || dm.Length == 0) continue;
                    if (depth == 0) len = dm.Length;
                    for (int i = 0; i < dm.Length; i++)
                    {
                        uint v = dm[i];
                        if (v == InferIni || v == InquiryInfer) { found = true; Main.Log.Msg($"  deduction command in script {id:X} (depth {depth})"); break; }
                        if (depth < 2 && v >= 0x100 && v < 0x4000 && v != id) queue.Enqueue((v, depth + 1));
                    }
                }
            }
            catch (Exception e) { Main.Log.Warning("Script check failed: " + e.Message); }
            _deductionCache[t.CallA] = found;
            Main.Log.Msg($"Script for {t.Num} ({ObjNames.Raw(t.Num)}) callA={t.CallA:X}: length {len}, deduction {found}");
            try
            {
                var dm2 = Game?.GetDM((int)t.CallA);
                if (dm2 != null) { var hx = new List<string>(); for (int i = 0; i < dm2.Length && i < 200; i++) hx.Add(dm2[i].ToString("X")); Main.Log.Msg("  script: " + string.Join(" ", hx)); }
            }
            catch { }
            return found;
        }

        /// <summary>[ and ] while walking around.</summary>
        public static void Step(int dir)
        {
            var g = Game;
            if (g == null || !Investigation.InInvestigation) return;
            try
            {
                uint map = CurrentMap(g);
                Refresh(g);
                var me = g.GetMyCharBuff();
                if (_targets.Count == 0 || me == null) { Speech.Say("Nothing to examine here.", true, false); return; }
                int cur = _targets.FindIndex(t => t.Num == _selNum);
                if (map != _map) { _map = map; cur = -1; }
                int sel = cur < 0 ? (dir > 0 ? 0 : _targets.Count - 1) : (cur + dir + _targets.Count) % _targets.Count;
                var tg = _targets[sel];
                _selNum = tg.Num;
                double px = Px(me.xof), py = Px(me.yof);
                Main.Log.Msg($"Nav select {sel + 1}/{_targets.Count}: num={tg.Num} ({ObjNames.Raw(tg.Num)}) at {tg.X:0},{tg.Y:0}; me {px:0},{py:0}");
                Speech.Say(Menus.Join(Describe(tg, px, py, map), $"{sel + 1} of {_targets.Count}"), true, false);
            }
            catch (Exception e) { Main.Log.Error("Nav step failed: " + e); }
        }

        // ---------- Walking ----------

        private static bool _walking;
        private static Target _walkTarget;
        private static List<Vector2> _path;
        private static int _pathPos, _replans;
        private static float _walkStart, _lastProgressAt;
        private static double _bestDist;
        private static string _promptAtStart;
        private static int _keysDown;   // bit 0 W, 1 A, 2 S, 3 D
        private static bool _facing;
        private static float _faceUntil;
        // Walking out through an exit that didn't work at first: try each direction in turn.
        private static Target _exitTry;
        private static uint _exitTryMap;
        private static int _exitTryStep;
        private static float _exitTryUntil;
        private static readonly int[] ExitDirs = { 1, 2, 8, 4, 3, 9, 6, 12 };

        /// <summary>
        /// Apostrophe: a sound from where the selected thing is. Left/right is stereo, higher means it's
        /// further up the room, lower means further down, louder means closer. Two rising notes: you're there.
        /// </summary>
        // Tracking: the sound repeats, faster as you get closer, until the key is pressed again.
        private static bool _tracking;
        private static int _trackNum = -1;
        private static float _nextPing;

        public static void Ping()
        {
            if (_tracking) { _tracking = false; Speech.Say("Tracking off.", true, false); return; }
            var g = Game;
            if (g == null || !Investigation.InInvestigation) return;
            try
            {
                Refresh(g);
                var t = _targets.Find(x => x.Num == _selNum);
                if (t == null || g.GetMyCharBuff() == null) { Speech.Say("Choose something first with the bracket keys.", true, false); return; }
                _tracking = true; _trackNum = t.Num; _nextPing = Time.unscaledTime + 1.2f;
                var me0 = g.GetMyCharBuff();
                Speech.Say(Menus.Join("Tracking " + t.Name, Spoken(t, Px(me0.xof), Px(me0.yof))), true, false);
            }
            catch (Exception e) { Main.Log.Error("Nav ping failed: " + e.Message); }
        }

        private static void PollTracking(GK1::Il2Cpp.GKJ1 g)
        {
            if (!_tracking) return;
            float now = Time.unscaledTime;
            if (now < _nextPing) return;
            // Quiet during conversations, menus and close-ups; carry on afterwards.
            if (!Investigation.PlayerCanWalk) { _nextPing = now + 0.3f; return; }
            // Don't cut off the Examine / Talk sound that just played.
            if (now - Earcons.LastPromptSoundAt < 0.5f) { _nextPing = now + 0.2f; return; }
            if (_selNum != _trackNum) _trackNum = _selNum;   // follow a new choice from the bracket keys
            uint map = CurrentMap(g);
            if (map != _map && _map != uint.MaxValue) { _tracking = false; Speech.Say("Tracking off, new room.", false, false); return; }
            Refresh(g);
            var t = _targets.Find(x => x.Num == _trackNum);
            var me = g.GetMyCharBuff();
            if (t == null || me == null) { _tracking = false; Speech.Say("Tracking off, it's gone.", false, false); return; }
            double px = Px(me.xof), py = Px(me.yof);
            var c = t.Exit ? NearestPoint(t, px, py) : HitCentre(t);
            double dist = Math.Sqrt((c.x - px) * (c.x - px) + (c.y - py) * (c.y - py));
            bool there = t.Exit ? InQuad(t.Hit, px, py) || InQuad(t.Flg, px, py) : InQuad(t.Flg, px, py) && ClearView(px, py, t);
            // Its own kind of prompt is showing and it's the closest thing of that kind: you're there.
            string pr = Investigation.CurrentPrompt;
            if (!there && !t.Exit && !string.IsNullOrEmpty(pr) && !PromptIsOther(t) && (Same(_promptTarget, t) || (_promptTarget == null && dist < 40))) there = true;
            if (!there)
            {
                // The sound comes from the next corner of the route the walk would take, so following it
                // leads around walls and things.
                var here = new Vector2((float)px, (float)py);
                List<Vector2> route;
                // Work the route out again only after moving a little (it takes a moment).
                if (_trkRoute != null && _trkFor == t.Num && (here - _trkFrom).magnitude < 10 && now - _trkAt < 2f) route = _trkRoute;
                else { route = Route(t, px, py); _trkRoute = route; _trkFor = t.Num; _trkFrom = here; _trkAt = now; _trkApproach = RouteApproach; }
                bool approach = _trkApproach;
                // Corners already passed don't count.
                while (route != null && route.Count > 1 && (route[0] - here).magnitude < 6) route = route.GetRange(1, route.Count - 1);
                if (route != null && route.Count > 0)
                {
                    double len = 0; var prevPt = new Vector2((float)px, (float)py);
                    foreach (var pt in route) { len += (pt - prevPt).magnitude; prevPt = pt; }
                    Vector2 aim = route[route.Count - 1];
                    foreach (var pt in route) if ((pt - new Vector2((float)px, (float)py)).magnitude > 8) { aim = pt; break; }
                    if (len < 6 && !approach) there = true;
                    else if (len < 6) aim = c;   // the end of the way; the thing itself is just beyond
                    c = aim; dist = len;
                }
            }
            double dx = c.x - px, dy = c.y - py;
            double pan = dx / 100.0;
            double freq = 523 * Math.Pow(2, Math.Max(-1, Math.Min(1, -dy / 100.0)));
            double vol = 0.25 + 0.75 * (1 - Math.Min(dist, 250) / 250);
            Earcons.PlayPositional(pan, freq, vol, there ? 2 : 1);
            // Like a parking sensor: about every 1.2 seconds far away, every 0.35 seconds close by.
            float interval = there ? 1.2f : 0.35f + 0.85f * (float)(Math.Min(dist, 200) / 200);
            _nextPing = now + interval;
        }

        /// <summary>A moment after arriving: did its own prompt come up? If a neighbour's did, try another spot.</summary>
        private static void CheckArrival(GK1::Il2Cpp.GKJ1 g)
        {
            var t = _verify; _verify = null;
            if (!Investigation.PlayerCanWalk) { _verifyTries = 0; return; }
            int gp = GamePromptNum(g);
            bool otherThing = gp >= 0 && gp != t.Num;
            if (!PromptIsOther(t) && !string.IsNullOrEmpty(Investigation.CurrentPrompt) && !otherThing) { _verifyTries = 0; return; }
            string sk = SpotKey(_map, t.Num);
            if (!_badGoal.TryGetValue(sk, out var l)) _badGoal[sk] = l = new List<Vector2>();
            l.Add(_verifyGoal);
            if (_goodSpot.TryGetValue(sk, out var gs) && (gs - _verifyGoal).magnitude < 10) _goodSpot.Remove(sk);
            Main.Log.Msg($"Nav: wrong prompt '{Investigation.CurrentPrompt}' at {_verifyGoal.x:0},{_verifyGoal.y:0} for {t.Num}; try {_verifyTries + 1}");
            if (++_verifyTries > 8)
            {
                _verifyTries = 0;
                Main.Log.Msg($"Gave up on {t.Num} {ObjNames.Raw(t.Num)}: flg {Show(t.Flg)} hit {Show(t.Hit)} pos {t.X:0},{t.Y:0}");
                Speech.Say("Couldn't find a spot where " + t.Name + " can be examined.", true, false);
                return;
            }
            var me = g.GetMyCharBuff(); if (me == null) return;
            double px = Px(me.xof), py = Px(me.yof);
            _walkTarget = t;
            _plannedNoWalls = false;
            if (!Plan(px, py)) { _verifyTries = 0; return; }
            _walking = true; _exitTry = null; _replans = 0;
            _walkStart = _lastProgressAt = Time.unscaledTime;
            _bestDist = double.MaxValue;
        }

        /// <summary>Backslash: walk to the selected thing (press again to stop).</summary>
        public static void WalkToSelected()
        {
            if (_walking) { StopWalk("Stopped."); return; }
            var g = Game;
            if (g == null || !Investigation.InInvestigation) return;
            try
            {
                Refresh(g);
                var t = _targets.Find(x => x.Num == _selNum);
                if (t == null) { Speech.Say("Choose something first with the bracket keys.", true, false); return; }
                if (t.Off) { Speech.Say("The game isn't letting you use " + t.Name + " right now.", true, false); return; }
                var me = g.GetMyCharBuff();
                if (me == null) return;
                double px = Px(me.xof), py = Px(me.yof);
                if (!t.Exit && InQuad(t.Flg, px, py) && ClearView(px, py, t) && !PromptIsOther(t)) { Speech.Say("You're already there.", true, false); FaceTarget(t, px, py); return; }
                _walkTarget = t; _verifyTries = 0; _verify = null;
                uint map = CurrentMap(g);
                if (map != _learnedMap) { _learned.Clear(); _learnedMap = map; }
                LoadWalls(g, map, px, py);
                _plannedNoWalls = false;
                if (!Plan(px, py)) { Speech.Say("Can't find a way to " + t.Name + ".", true, false); return; }
                _walking = true; _exitTry = null; _replans = 0;
                _walkStart = _lastProgressAt = Time.unscaledTime;
                _bestDist = double.MaxValue;
                _promptAtStart = Investigation.CurrentPrompt;
                Speech.Say("Walking to " + t.Name, true, false);
            }
            catch (Exception e) { Main.Log.Error("Nav walk failed: " + e); Speech.Say("Can't walk there.", true, false); StopWalk(null); }
        }

        // Walls the game's collision has stopped us at (cells), learned per room.
        private static readonly HashSet<int> _learned = new HashSet<int>();
        private static uint _learnedMap = uint.MaxValue;

        // The room's own walls, from the game's floor check (null when we couldn't make sense of it).
        private static bool[,] _wall;
        private static uint _wallMap = uint.MaxValue;

        private static void LoadWalls(GK1::Il2Cpp.GKJ1 g, uint map, double px, double py)
        {
            if (map == _wallMap) return;
            _wallMap = map; _wall = null;
            int w = _mapW / Cell + 1, h = _mapH / Cell + 1;
            foreach (bool fixedPt in new[] { false, true })
            {
                try
                {
                    Func<double, double, bool> hit = (x, y) => fixedPt ? g.MapHitCheckPos((int)(x * 65536), (int)(y * 65536)) : g.MapHitCheckPos((int)x, (int)y);
                    bool floor = hit(px, py);   // whatever the check says where Edgeworth stands means "floor"
                    var grid = new bool[w, h];
                    int walls = 0;
                    for (int cy = 0; cy < h; cy++)
                        for (int cx = 0; cx < w; cx++)
                        {
                            bool v = hit(cx * Cell + Cell / 2.0, cy * Cell + Cell / 2.0) != floor;
                            grid[cx, cy] = v; if (v) walls++;
                        }
                    var sb = new System.Text.StringBuilder();
                    for (int cy = 0; cy < h; cy += 2)
                    {
                        for (int cx = 0; cx < w; cx++) sb.Append(grid[cx, cy] ? '#' : '.');
                        sb.Append('\n');
                    }
                    Main.Log.Msg($"Nav floor check ({(fixedPt ? "fixed" : "pixels")}), floor={floor}, {walls} of {w * h} cells are wall:\n{sb}");
                    if (_wall == null && walls > w * h / 20 && walls < w * h * 9 / 10) _wall = grid;
                }
                catch (Exception e) { Main.Log.Msg("Nav floor check failed: " + e.Message); }
            }
            Main.Log.Msg(_wall == null ? "Nav: not using the floor check" : "Nav: using the floor check for walls");
        }

        private static bool WallAt(double x, double y)
        {
            if (_wall == null) return false;
            int cx = (int)(x / Cell), cy = (int)(y / Cell);
            if (cx < 0 || cy < 0 || cx >= _wall.GetLength(0) || cy >= _wall.GetLength(1)) return true;
            return _wall[cx, cy];
        }

        // Solid things Edgeworth is already touching when the walk starts (only their core counts, so he can step away).
        private static readonly List<Vector2[]> _ignore = new List<Vector2[]>();
        private static bool Near(Vector2[] s, double x, double y) =>
            InQuad(s, x, y) || InQuad(s, x - 6, y) || InQuad(s, x + 6, y) || InQuad(s, x, y - 3) || InQuad(s, x, y + 3);

        private static bool Blocked(int cx, int cy, Target target)
        {
            if (_learned.Contains(cy * 1000 + cx)) return true;
            double x = cx * Cell + Cell / 2.0, y = cy * Cell + Cell / 2.0;
            if (WallAt(x, y)) return true;   // the game checks walls at Edgeworth's feet
            if (x < 4 || y < 4 || x > _mapW - 4 || y > _mapH - 2) return true;
            // Edgeworth's own size (about 10 x 6 each way): test his corners against the solid areas.
            if (_noSolids) return false;
            foreach (var s in _solids)
            {
                if ((target != null && s == target.Hit) || _ignore.Contains(s)) { if (InQuad(s, x, y)) return true; continue; }
                if (Near(s, x, y)) return true;
            }
            return false;
        }

        /// <summary>Breadth-first search on a small grid from the player to any cell inside the target's check area.</summary>
        /// <summary>The 8-way direction Edgeworth faces when looking from (x, y) at the thing.</summary>
        private static Vector2 FaceDir(double x, double y, Target t, out int keys)
        {
            var c = HitCentre(t);
            double dx = c.x - x, dy = c.y - y, ax = Math.Abs(dx), ay = Math.Abs(dy);
            keys = 0; float vx = 0, vy = 0;
            if (ax > ay * 0.4) { keys |= dx < 0 ? 2 : 8; vx = dx < 0 ? -1 : 1; }
            if (ay > ax * 0.4) { keys |= dy < 0 ? 1 : 4; vy = dy < 0 ? -1 : 1; }
            return new Vector2(vx, vy).normalized;
        }

        /// <summary>Does Edgeworth's body (about 16 x 10 pixels) standing here touch this check area? The game brings
        /// a thing's prompt up when he overlaps its area, not only when his position point is inside it.</summary>
        private static bool BoxIn(Vector2[] q, double x, double y)
        {
            if (Empty(q)) return false;
            for (int ox = -8; ox <= 8; ox += 4)
                for (int oy = -5; oy <= 5; oy += 5)
                    if (InQuad(q, x + ox, y + oy)) return true;
            return false;
        }

        /// <summary>Would Edgeworth (about 20 x 12 pixels) standing here also be in someone else's check area?</summary>
        private static bool InOthersArea(double x, double y, Target t)
        {
            foreach (var o in _targets)
            {
                if (Same(o, t) || o.Exit) continue;
                for (int ox = -10; ox <= 10; ox += 5)
                    for (int oy = -6; oy <= 6; oy += 6)
                        if (InQuad(o.Flg, x + ox, y + oy)) return true;
            }
            return false;
        }

        /// <summary>Looking from (x, y) toward the thing, is it the first thing in the way?</summary>
        private static bool ClearView(double x, double y, Target t)
        {
            var d = FaceDir(x, y, t, out _);
            for (int k = 2; k <= 48; k += 2)
            {
                double px = x + d.x * k, py = y + d.y * k;
                if (InQuad(t.Hit, px, py)) return true;
                foreach (var o in _targets)
                    if (!Same(o, t) && !o.Exit && InQuad(o.Hit, px, py)) return false;
            }
            return false;
        }

        private static string Show(Vector2[] q) => q == null ? "none" : string.Join(" ", Array.ConvertAll(q, v => $"{v.x:0},{v.y:0}"));

        private static bool NearBad(Target t, double x, double y)
        {
            if (!_badGoal.TryGetValue(SpotKey(_map, t.Num), out var l)) return false;
            foreach (var b in l) if (Math.Abs(b.x - x) < 14 && Math.Abs(b.y - y) < 14) return true;
            return false;
        }

        private static Vector2 HitCentre(Target t)
        {
            if (Empty(t.Hit)) return new Vector2((float)t.X, (float)t.Y);
            return (t.Hit[0] + t.Hit[1] + t.Hit[2] + t.Hit[3]) / 4f;
        }

        private static bool _noSolids;
        private static bool _plannedNoWalls;

        /// <summary>Log the area around a thing that couldn't be reached: # wall, o something's area, r reached, . free, E/R its area.</summary>
        private static void DumpAround(Target t, int[] prev, int w, int h)
        {
            try
            {
                var hc2 = HitCentre(t);
                int y0 = Math.Max(0, (int)(hc2.y / Cell) - 10), y1 = Math.Min(h - 1, (int)(hc2.y / Cell) + 14);
                var sb = new System.Text.StringBuilder();
                for (int cy = y0; cy <= y1; cy++)
                {
                    sb.Append((cy * Cell).ToString().PadLeft(4)).Append(' ');
                    for (int cx = 0; cx < w; cx++)
                    {
                        double x = cx * Cell + Cell / 2.0, y = cy * Cell + Cell / 2.0;
                        char ch = prev[cy * w + cx] != -2 ? 'r' : WallAt(x, y) ? '#' : Blocked(cx, cy, t) ? 'o' : '.';
                        if (InQuad(t.Hit, x, y)) ch = ch == 'r' ? 'R' : 'E';
                        else if (InQuad(t.Flg, x, y)) ch = ch == 'r' ? 'F' : 'f';
                        sb.Append(ch);
                    }
                    sb.Append('\n');
                }
                NLog($"Nav: {t.Num} {ObjNames.Raw(t.Num)} not reached (flg {Q(t.Flg)} hit {Q(t.Hit)}); around it:\n{sb}");
            }
            catch { }
        }
        private static bool _quiet;
        private static void NLog(string m) { if (!_quiet) Main.Log.Msg(m); }

        /// <summary>The route the walk would take from (px, py) to t (corner points), without walking; null if none.</summary>
        private static List<Vector2> Route(Target t, double px, double py)
        {
            var g = Game;
            if (g == null) return null;
            var keepT = _walkTarget; var keepP = _path; int keepPos = _pathPos; bool keepA = _approach;
            var keepI = new List<Vector2[]>(_ignore);
            _quiet = true;
            try
            {
                uint map = CurrentMap(g);
                if (map != _learnedMap) { _learned.Clear(); _learnedMap = map; }
                LoadWalls(g, map, px, py);
                _walkTarget = t;
                if (!Plan(px, py)) return null;
                var r = new List<Vector2>(_path);
                RouteApproach = _approach;
                return r;
            }
            catch { return null; }
            finally
            {
                _quiet = false;
                _walkTarget = keepT; _path = keepP; _pathPos = keepPos; _approach = keepA;
                _ignore.Clear(); _ignore.AddRange(keepI);
            }
        }
        private static bool RouteApproach;
        private static List<Vector2> _trkRoute; private static int _trkFor = -1; private static Vector2 _trkFrom; private static float _trkAt; private static bool _trkApproach;

        /// <summary>A route as walking directions: "5 right, then 8 up".</summary>
        private static string Legs(List<Vector2> route, double px, double py, out double length)
        {
            length = 0;
            var legs = new List<(string dir, double d)>();
            var prev = new Vector2((float)px, (float)py);
            foreach (var pt in route)
            {
                double dx = pt.x - prev.x, dy = pt.y - prev.y;
                double ax = Math.Abs(dx), ay = Math.Abs(dy);
                if (ax + ay < 0.5) { prev = pt; continue; }
                string h = dx < 0 ? "left" : "right", v = dy < 0 ? "up" : "down";
                string dir; double d;
                if (ax > ay * 2.5) { dir = h; d = ax; }
                else if (ay > ax * 2.5) { dir = v; d = ay; }
                else { dir = v + "-" + h; d = Math.Max(ax, ay); }
                length += Math.Sqrt(dx * dx + dy * dy);
                if (legs.Count > 0 && legs[legs.Count - 1].dir == dir) legs[legs.Count - 1] = (dir, legs[legs.Count - 1].d + d);
                else legs.Add((dir, d));
                prev = pt;
            }
            var parts = new List<string>();
            double carry = 0;
            foreach (var l in legs)
            {
                int n = (int)Math.Round((l.d + carry) / StepSize);
                if (n == 0) { carry += l.d; continue; }
                carry = 0;
                parts.Add($"{n} {l.dir}");
            }
            if (parts.Count == 0) return "right here";
            if (parts.Count > 3) { parts.RemoveRange(3, parts.Count - 3); parts.Add("and on"); }
            return string.Join(", then ", parts);
        }
        // The route ends near the thing, not in its examine area: push toward it on arrival.
        private static bool _approach;
        private static Target _faceTarget;
        // Sliding round something in front of the thing: which way we're trying now (-1: not yet).
        private static int _apStep = -1;
        private static int[] _apKeys = new int[0];

        private static void ApproachNext(GK1::Il2Cpp.GKJ1 g)
        {
            var t = _faceTarget;
            if (_apStep == 0)
            {
                // From where we are: up-left, up-right, left, right, toward it again (with the axis it needs).
                var me = g.GetMyCharBuff(); if (me == null) { _apStep = -1; return; }
                var c = HitCentre(t); double dy = c.y - Px(me.yof);
                int v = dy < 0 ? 1 : 4;
                int u = v == 1 ? 4 : 1;
                _apKeys = new[] { v | 2, v | 8, 2, 8, 2 | u, 8 | u, v | 2, v | 8, u, v, 2, v, 8, v, u | 2, v | 8, u | 8, v | 2 };
            }
            if (_apStep >= _apKeys.Length)
            {
                _apStep = -1; _approach = false; _verify = null;
                Main.Log.Msg($"Nav: couldn't get {t.Num}'s own prompt");
                Speech.Say(t.Off ? "The game isn't letting you use " + t.Name + " right now." : "Couldn't get to " + t.Name + ". Something else is in front of it.", true, false);
                return;
            }
            int k = _apKeys[_apStep++];
            var m2 = g.GetMyCharBuff();
            Main.Log.Msg($"Nav: sliding toward {t.Num} with keys {k} from {(m2 == null ? "?" : $"{Px(m2.xof):0},{Px(m2.yof):0}")} (prompt '{Investigation.CurrentPrompt}')");
            SetKeys(k);
            _facing = true; _faceUntil = Time.unscaledTime + 0.5f;
        }

        private static bool Plan(double px, double py)
        {
            var t = _walkTarget;
            int w = _mapW / Cell + 1, h = _mapH / Cell + 1;
            _ignore.Clear();
            foreach (var sd in _solids) if (Near(sd, px, py)) _ignore.Add(sd);
            var prev = new int[w * h];
            for (int i = 0; i < prev.Length; i++) prev[i] = -2;
            int sx = Mathf.Clamp((int)(px / Cell), 0, w - 1), sy = Mathf.Clamp((int)(py / Cell), 0, h - 1);
            var q = new Queue<int>();
            q.Enqueue(sy * w + sx); prev[sy * w + sx] = -1;
            int goal = -1;
            double goalScore = double.MaxValue;
            var steps = new int[w * h];
            var hc = HitCentre(t);
            int[] dxs = { 1, -1, 0, 0, 1, 1, -1, -1 }, dys = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (q.Count > 0)
            {
                int c = q.Dequeue(); int cx = c % w, cy = c / w;
                double x = cx * Cell + Cell / 2.0, y = cy * Cell + Cell / 2.0;
                bool candidate = t.Exit ? InQuad(t.Hit, x, y) || (Empty(t.Hit) && InQuad(t.Flg, x, y)) : InQuad(t.Flg, x, y) && !InQuad(t.Hit, x, y);
                if (candidate)
                {
                    // Stand right next to it, facing it with nothing else in between
                    // (so its prompt, not a neighbour's, comes up), without a long detour.
                    double score = t.Exit ? steps[c]
                        : Math.Sqrt((x - hc.x) * (x - hc.x) + (y - hc.y) * (y - hc.y)) + steps[c] * 0.3 + (ClearView(x, y, t) ? 0 : 60)
                          + (InOthersArea(x, y, t) ? 200 : 0) + (NearBad(t, x, y) ? 1000 : 0);
                    if (score < goalScore) { goalScore = score; goal = c; }
                }
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + dxs[k], ny = cy + dys[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (prev[n] != -2 || Blocked(nx, ny, t)) continue;
                    // No cutting corners diagonally.
                    if (k >= 4 && (Blocked(cx + dxs[k], cy, t) || Blocked(cx, cy + dys[k], t))) continue;
                    prev[n] = c; steps[n] = steps[c] + 1; q.Enqueue(n);
                }
            }
            // An exit the walkable floor doesn't quite reach (it's at the edge of the room): go as close as we can
            // get, then keep walking toward it.
            if (goal < 0 && t.Exit && !_noSolids)
            {
                DumpAround(t, prev, w, h);
                // Things' areas may be smaller than they look: try again counting only the walls.
                _noSolids = true;
                bool ok;
                try { ok = Plan(px, py); } finally { _noSolids = false; }
                if (ok) { NLog($"Nav: exit {t.Num} reachable when things' areas are ignored"); return true; }
            }
            if (goal < 0 && _noSolids && t.Exit) return false;
            _approach = false;
            if (goal < 0 && !t.Exit)
            {
                // Its examine area is all inside walls or other things (common at the room's edge): get as close
                // as we can, then push toward it until its prompt comes up.
                double bd = 80, bdAny = 64; int goalAny = -1;
                var hcN = HitCentre(t);
                for (int c = 0; c < prev.Length; c++)
                {
                    if (prev[c] == -2) continue;
                    double x = c % w * Cell + Cell / 2.0, y = c / w * Cell + Cell / 2.0;
                    double dd = Math.Sqrt((hcN.x - x) * (hcN.x - x) + (hcN.y - y) * (hcN.y - y));
                    if (NearBad(t, x, y)) dd += 30;
                    if (dd >= bd && dd >= bdAny) continue;
                    if (dd < bdAny) { bdAny = dd; goalAny = c; }
                    // Best: facing it from there, nothing else is in the way (so its prompt, not a neighbour's, comes up).
                    if (dd < bd && ClearView(x, y, t)) { bd = dd; goal = c; }
                }
                if (goal < 0) { goal = goalAny; bd = bdAny; }
                if (goal >= 0) { _approach = true; NLog($"Nav: can't reach the examine area of {t.Num} (flg {Q(t.Flg)} hit {Q(t.Hit)}); going {bd:0} px from it"); }
                else if (!_noSolids)
                {
                    DumpAround(t, prev, w, h);
                    _noSolids = true;
                    bool ok2;
                    try { ok2 = Plan(px, py); } finally { _noSolids = false; }
                    if (ok2) { NLog($"Nav: {t.Num} reachable when things' areas are ignored"); return true; }
                }
            }
            if (goal < 0 && _noSolids) return false;
            if (goal < 0 && t.Exit)
            {
                double bd = 80;
                for (int c = 0; c < prev.Length; c++)
                {
                    if (prev[c] == -2) continue;
                    double x = c % w * Cell + Cell / 2.0, y = c / w * Cell + Cell / 2.0;
                    var np = NearestPoint(t, x, y);
                    double dd = Math.Sqrt((np.x - x) * (np.x - x) + (np.y - y) * (np.y - y));
                    if (dd < bd) { bd = dd; goal = c; }
                }
                if (goal >= 0) NLog($"Nav: exit {t.Num} is off the floor; going {bd:0} px from it");
            }
            // A place where this thing's own prompt has come up before beats any guess.
            if (!t.Exit && _goodSpot.TryGetValue(SpotKey(_map, t.Num), out var gs))
            {
                int gx = Mathf.Clamp((int)(gs.x / Cell), 0, w - 1), gy = Mathf.Clamp((int)(gs.y / Cell), 0, h - 1);
                if (prev[gy * w + gx] != -2) { goal = gy * w + gx; NLog($"Nav: going to the known good spot {gs.x:0},{gs.y:0}"); }
            }
            if (goal < 0)
            {
                NLog($"Nav: no path from {px:0},{py:0} to num={t.Num} (walls={_wall != null}, learned={_learned.Count})");
                // Our idea of the walls may be wrong: try again with less of it.
                if (_learned.Count > 0 && !_quiet) { _learned.Clear(); return Plan(px, py); }
                if (_wall != null) { var keep = _wall; _wall = null; bool ok = Plan(px, py); _wall = keep; if (ok && !_quiet) _plannedNoWalls = true; return ok; }
                return false;
            }
            var path = new List<Vector2>();
            if (goal == sy * w + sx) path.Add(new Vector2((float)px, (float)py));
            for (int c = goal; c >= 0; c = prev[c]) path.Add(new Vector2(c % w * Cell + Cell / 2f, c / w * Cell + Cell / 2f));
            path.Reverse();
            // Keep only the corners of the route.
            var simple = new List<Vector2>();
            for (int i = 1; i < path.Count; i++)
            {
                if (i == path.Count - 1) { simple.Add(path[i]); break; }
                var d1 = path[i] - path[i - 1]; var d2 = path[i + 1] - path[i];
                if (d1 != d2) simple.Add(path[i]);
            }
            _path = simple; _pathPos = 0;
            NLog($"Nav path to num={t.Num} ({t.Name}): " + string.Join(" ", simple.ConvertAll(p => $"{p.x:0},{p.y:0}")));
            return simple.Count > 0;
        }

        public static void Poll()
        {
            var g = Game;
            if (g == null) return;
            try
            {
                PollTracking(g);
                string prompt = Investigation.CurrentPrompt;
                if (!string.IsNullOrEmpty(prompt) && prompt != _lastPromptSeen)
                {
                    // Remember the checked state of the thing whose check area we are standing in.
                    var me = g.GetMyCharBuff();
                    if (me != null)
                    {
                        uint map = CurrentMap(g);
                        Refresh(g);
                        double px = Px(me.xof), py = Px(me.yof);
                        bool talk = prompt.StartsWith("Talk");
                        Target here = null; double hd = double.MaxValue; int candidates = 0;
                        foreach (var cand in _targets)
                        {
                            if (cand.Exit || cand.Person != talk || !BoxIn(cand.Flg, px, py)) continue;
                            candidates++;
                            var c = HitCentre(cand);
                            double dd = (c.x - px) * (c.x - px) + (c.y - py) * (c.y - py);
                            if (_walkedTo == cand.Num) dd = -1;   // we just walked to it
                            if (dd < hd) { hd = dd; here = cand; }
                        }
                        // Only trust it when there's no doubt which thing the prompt is for.
                        bool sure = here != null && (candidates == 1 || _walkedTo == here.Num || (_walking && Same(_walkTarget, here)));
                        // Best of all: the game's own record of which thing its prompt is for.
                        int gameNum = GamePromptNum(g);
                        if (gameNum >= 0)
                        {
                            var gt = _targets.Find(c => !c.Exit && c.Num == gameNum);
                            if (gt != null) { if (here == null || here.Num != gt.Num) Main.Log.Msg($"Prompt: the game says it's {gameNum} {ObjNames.Raw(gameNum)} (guessed {(here == null ? "?" : here.Num.ToString())})"); here = gt; sure = true; }
                            else Main.Log.Msg($"Prompt: the game says {gameNum} {ObjNames.Raw(gameNum)}, not in the list");
                        }
                        // The prompt often flickers (e.g. "Talk" then "Talk, checked"): only learn from it once it's steady.
                        if (sure) { _pendTarget = here; _pendPrompt = prompt; _pendMap = map; _pendAt = Time.unscaledTime; _pendPos = new Vector2((float)px, (float)py); }
                        else _pendTarget = null;
                        _promptTarget = sure ? here : null; _promptKey = sure ? Key(map, here.Num, here.CallA) : null;
                        Main.Log.Msg($"Prompt '{prompt}' at {px:0},{py:0} dir={me.dir}; belongs to {(here == null ? "?" : here.Num + " " + ObjNames.Raw(here.Num))}");
                    }
                }
                _lastPromptSeen = prompt;
                if (string.IsNullOrEmpty(prompt) && Investigation.PlayerCanWalk) _promptTarget = null;
                // Examining / talking: the game only updates its check mark the next time the prompt opens,
                // so when a conversation started from the prompt is over, count that thing as checked.
                bool canWalk = Investigation.PlayerCanWalk;
                if (_couldWalk && !canWalk && _promptTarget != null && !string.IsNullOrEmpty(prompt))
                { _usingTarget = _promptTarget; _usingKey = _promptKey; _usingSince = Time.unscaledTime; _convSpeakers.Clear(); _convOrder.Clear(); Dialogue.LastSpeaker = ""; }
                if (!_couldWalk && canWalk && _usingTarget != null)
                {
                    if (Dialogue.LastSpeakerAt > _usingSince)
                    {
                        Main.Log.Msg($"Finished with {_usingTarget.Num} {ObjNames.Raw(_usingTarget.Num)}: now checked");
                        if (_usingTarget.Person) LearnFromConversation(_usingTarget);
                        _checked[_usingKey] = true;
                        SaveChecked();
                    }
                    _usingTarget = null;
                }
                _couldWalk = canWalk;
                if (_pendTarget != null && prompt != _pendPrompt) _pendTarget = null;
                if (_pendTarget != null && Time.unscaledTime - _pendAt > 0.25f)
                {
                    var here = _pendTarget; _pendTarget = null;
                    bool done = _pendPrompt.EndsWith("checked");
                    string sk = SpotKey(_pendMap, here.Num);
                    if (!_goodSpot.ContainsKey(sk)) { Main.Log.Msg($"Good spot for {here.Num}: {_pendPos.x:0},{_pendPos.y:0}"); _goodSpot[sk] = _pendPos; }
                    _checked[Key(_pendMap, here.Num, here.CallA)] = done;
                    // Prefer what the game itself just set on the prompt, if the hook saw it.
                    if (Time.unscaledTime - _hookAt < 1f && _hookRead != done) { Main.Log.Msg($"Prompt said {done}, game set {_hookRead}"); done = _hookRead; _checked[Key(_pendMap, here.Num, here.CallA)] = done; }
                    Score(here, done);
                    SaveChecked();
                    if (_pendPrompt.StartsWith("Talk")) { _talkNum = here.Num; _talkAt = Time.unscaledTime; _talkSpeakerBefore = Dialogue.LastSpeaker; }
                }
                LearnName();
                if (_walkedTo >= 0 && Time.unscaledTime - _walkedAt > 1.5f) _walkedTo = -1;

                // Pushing toward a thing: stop as soon as a prompt of its kind shows.
                bool ownPrompt = false;
                if (_facing && _faceTarget != null && _approach && !string.IsNullOrEmpty(prompt) && !PromptIsOther(_faceTarget))
                {
                    var mq = g.GetMyCharBuff();
                    if (mq != null)
                    {
                        double qx = Px(mq.xof), qy = Px(mq.yof);
                        // Its own prompt: standing in its area with nothing else in front, or the game's prompt
                        // can't be placed on anything else.
                        int gpNow = GamePromptNum(g);
                        if (gpNow >= 0) ownPrompt = gpNow == _faceTarget.Num;   // the game says whose prompt it is
                        else ownPrompt = (InQuad(_faceTarget.Flg, qx, qy) && ClearView(qx, qy, _faceTarget)) || Same(_promptTarget, _faceTarget)
                            || (_promptTarget == null && OwnerName(_faceTarget.Person).Length == 0);
                        if (!ownPrompt && _apStep < 0) { _apStep = 0; _faceUntil = Time.unscaledTime; Main.Log.Msg($"Nav: a neighbour's prompt ('{prompt}', {_promptTarget?.Num}) while pushing toward {_faceTarget.Num}; sliding round it"); }
                    }
                }
                if (ownPrompt)
                {
                    var mp = g.GetMyCharBuff();
                    Main.Log.Msg($"Nav: prompt '{prompt}' came up while pushing toward {_faceTarget.Num}{(mp == null ? "" : $" at {Px(mp.xof):0},{Px(mp.yof):0}")}");
                    if (mp != null) { string sk = SpotKey(_map, _faceTarget.Num); _goodSpot[sk] = new Vector2((float)Px(mp.xof), (float)Px(mp.yof)); }
                    SetKeys(0); _facing = false; _approach = false; _apStep = -1;
                }
                if (_facing && Time.unscaledTime >= _faceUntil)
                {
                    SetKeys(0); _facing = false;
                    if (_exitTry != null) ExitNextTry(g);
                    else if (_approach && _faceTarget != null && _apStep >= 0) ApproachNext(g);
                    else if (_approach && _faceTarget != null && string.IsNullOrEmpty(prompt)) { _apStep = 0; ApproachNext(g); }
                }
                if (_verify != null && !_walking && !_facing && Time.unscaledTime >= _verifyAt) CheckArrival(g);
                if (!_walking) return;
                if (!Investigation.InInvestigation || !Main.GameHasFocus) { StopWalk(null); return; }

                var p = g.GetMyCharBuff();
                if (p == null) { StopWalk(null); return; }
                double x = Px(p.xof), y = Px(p.yof);
                float now = Time.unscaledTime;
                var t = _walkTarget;

                // Arrived: inside the check area, or the prompt for something showed up.
                var end = _path[_path.Count - 1];
                double toEnd = Math.Sqrt((end.x - x) * (end.x - x) + (end.y - y) * (end.y - y));
                bool there = toEnd < 3 || (t.Exit && InQuad(t.Hit, x, y));
                // Its own kind of prompt came up on the way, inside its check area: stop right here, facing as we are
                // (walking on may bring up a neighbour's prompt instead).
                bool nearIt = false;
                if (!t.Exit && !string.IsNullOrEmpty(prompt) && !PromptIsOther(t) && !InQuad(t.Flg, x, y) && _promptTarget == null)
                {
                    // The game shows a prompt we can't place: if this thing is the closest of its kind, it's for it.
                    var hcT = HitCentre(t); double dT = (hcT.x - x) * (hcT.x - x) + (hcT.y - y) * (hcT.y - y);
                    nearIt = dT < 40 * 40;
                    foreach (var o in _targets)
                    {
                        if (Same(o, t) || o.Exit || o.Person != t.Person) continue;
                        var ho = HitCentre(o);
                        if ((ho.x - x) * (ho.x - x) + (ho.y - y) * (ho.y - y) < dT) { nearIt = false; break; }
                        // Touching another thing's area too: the prompt may well be that one's.
                        if (BoxIn(o.Flg, x, y)) { nearIt = false; break; }
                    }
                }
                int gpWalk = GamePromptNum(g);
                if (!t.Exit && gpWalk == t.Num && !string.IsNullOrEmpty(prompt))
                {
                    Main.Log.Msg($"Nav: the game shows {t.Num}'s prompt at {x:0},{y:0}; stopping here");
                    _promptTarget = t; _promptKey = Key(CurrentMap(g), t.Num, t.CallA);
                    SetKeys(0);
                    _walking = false; _walkedTo = t.Num; _walkedAt = now;
                    _goodSpot[SpotKey(_map, t.Num)] = new Vector2((float)x, (float)y);
                    return;
                }
                bool gameSaysOther = gpWalk >= 0 && gpWalk != t.Num;
                if (!t.Exit && !string.IsNullOrEmpty(prompt) && !PromptIsOther(t) && !gameSaysOther && (InQuad(t.Flg, x, y) || nearIt))
                {
                    Main.Log.Msg($"Nav: its prompt '{prompt}' came up at {x:0},{y:0}; stopping here");
                    _promptTarget = t; _promptKey = Key(CurrentMap(g), t.Num, t.CallA);
                    SetKeys(0);
                    _walking = false; _walkedTo = t.Num; _walkedAt = now;
                    string sk = SpotKey(_map, t.Num);
                    if (!_goodSpot.ContainsKey(sk)) { _goodSpot[sk] = new Vector2((float)x, (float)y); Main.Log.Msg($"Good spot for {t.Num}: {x:0},{y:0}"); }
                    return;
                }
                if (there)
                {
                    Main.Log.Msg($"Nav arrived at {x:0},{y:0} (prompt '{prompt}')");
                    _walking = false; _walkedTo = t.Num; _walkedAt = now;
                    FaceTarget(t, x, y);
                    if (!t.Exit) { _verify = t; _verifyAt = now + (_approach ? 1.4f : 0.9f); _verifyGoal = end; }

                    return;
                }

                double d = toEnd + (_path.Count - 1 - _pathPos) * 0.01;
                if (d < _bestDist - 1) { _bestDist = d; _lastProgressAt = now; }
                if (now - _lastProgressAt > 1.0f)
                {
                    // Something we can't see blocks the way: remember the spot just ahead and go around it.
                    var ahead = _path[_pathPos] - new Vector2((float)x, (float)y);
                    if (ahead.magnitude > 0.1f)
                    {
                        var dirv = ahead.normalized;
                        for (float k = 4; k <= 12; k += 2)
                        {
                            int bx = (int)((x + dirv.x * k) / Cell), by = (int)((y + dirv.y * k) / Cell);
                            for (int ox = -1; ox <= 1; ox++) for (int oy = -1; oy <= 1; oy++) _learned.Add((by + oy) * 1000 + bx + ox);
                        }
                    }
                    SetKeys(0);
                    // A route that only works if the walls aren't real: after it gets stuck twice, give up.
                    if (_plannedNoWalls && _replans >= 1) { StopWalk("Can't find a way to " + t.Name + "."); return; }
                    if (++_replans <= 8 && Plan(x, y)) { _lastProgressAt = now; _bestDist = double.MaxValue; Main.Log.Msg("Nav: stuck, new route"); }
                    else { StopWalk(ReadSteps ? Menus.Join("Stuck", t.Name + ": " + Where(t, x, y)) : "Stuck."); return; }
                }
                if (now - _walkStart > 30f) { StopWalk("Stopped."); return; }

                // Follow the route: head for the next corner.
                while (_pathPos < _path.Count - 1 && (_path[_pathPos] - new Vector2((float)x, (float)y)).magnitude < 3f) _pathPos++;
                var wp = _path[_pathPos];
                double dx = wp.x - x, dy = wp.y - y;
                int keys = 0;
                if (dy < -1.5) keys |= 1; else if (dy > 1.5) keys |= 4;
                if (dx < -1.5) keys |= 2; else if (dx > 1.5) keys |= 8;
                // Avoid wobbling: when one axis is much smaller, leave it for later.
                if (Math.Abs(dx) > 4 * Math.Abs(dy) && Math.Abs(dy) < 4) keys &= ~(1 | 4);
                if (Math.Abs(dy) > 4 * Math.Abs(dx) && Math.Abs(dx) < 4) keys &= ~(2 | 8);
                if (keys == 0 && _pathPos < _path.Count - 1) _pathPos++;
                SetKeys(keys);
            }
            catch (Exception e) { Main.Log.Error("Nav poll failed: " + e.Message); StopWalk(null); }
        }

        /// <summary>Tap toward the thing so Edgeworth faces it.</summary>
        private static void FaceTarget(Target t, double x, double y)
        {
            int keys;
            float hold = 0.06f;
            if (t.Exit)
            {
                // Keep walking on toward the exit (and out of the room).
                // Toward the exit's own position (its doorway), not just its area.
                double dx = t.X - x, dy = t.Y - y;
                if (Math.Abs(dx) < 3 && Math.Abs(dy) < 3)
                {
                    // Standing on it: out through the nearest edge of the room.
                    double l = x, r = _mapW - x, u = y, dn = _mapH - y, m = Math.Min(Math.Min(l, r), Math.Min(u, dn));
                    keys = m == dn ? 4 : m == u ? 1 : m == l ? 2 : 8;
                }
                else
                {
                    keys = 0;
                    if (Math.Abs(dx) >= Math.Abs(dy) * 0.4) keys |= dx < 0 ? 2 : 8;
                    if (Math.Abs(dy) >= Math.Abs(dx) * 0.4) keys |= dy < 0 ? 1 : 4;
                }
                // Toward the middle of its entry area, when it has one.
                if (t.Hit != null && t.Hit.Length == 4)
                {
                    double cx = 0, cy = 0; foreach (var v in t.Hit) { cx += v.x / 4; cy += v.y / 4; }
                    double ex = cx - x, ey = cy - y;
                    if (Math.Abs(ex) >= 3 || Math.Abs(ey) >= 3)
                    {
                        keys = 0;
                        if (Math.Abs(ex) >= Math.Abs(ey) * 0.4) keys |= ex < 0 ? 2 : 8;
                        if (Math.Abs(ey) >= Math.Abs(ex) * 0.4) keys |= ey < 0 ? 1 : 4;
                    }
                }
                Main.Log.Msg($"Nav: walking on into exit {t.Num} (keys {keys}); me {x:0},{y:0} flg {Q(t.Flg)} hit {Q(t.Hit)}");
                hold = 1.5f;
                _exitTry = t; _exitTryStep = -1;
                try { _exitTryMap = CurrentMap(GK1::Il2Cpp.AppCore.gkj1); } catch { _exitTryMap = uint.MaxValue; }
            }
            else
            {
                FaceDir(x, y, t, out keys);
                if (_approach) { hold = 1.0f; _apStep = -1; Main.Log.Msg($"Nav: pushing toward {t.Num} (keys {keys}) until its prompt shows"); }
            }
            SetKeys(keys);
            _faceTarget = t.Exit ? null : t;
            _facing = true; _faceUntil = Time.unscaledTime + hold;
        }

        private static string Q(Vector2[] q) => q == null ? "-" : string.Join(" ", Array.ConvertAll(q, v => $"{v.x:0},{v.y:0}"));

        /// <summary>The exit didn't take us out yet: push in the next direction, until the room changes.</summary>
        private static void ExitNextTry(GK1::Il2Cpp.GKJ1 g)
        {
            uint now = uint.MaxValue;
            try { now = CurrentMap(g); } catch { }
            if (now != _exitTryMap || !Investigation.PlayerCanWalk || !Main.GameHasFocus)
            {
                if (now != _exitTryMap) Main.Log.Msg($"Nav: through the exit ({(_exitTryStep < 0 ? "first push" : "direction " + ExitDirs[_exitTryStep])})");
                _exitTry = null; return;
            }
            if (++_exitTryStep >= ExitDirs.Length)
            {
                var p0 = g.GetMyCharBuff();
                Main.Log.Msg($"Nav: couldn't get through exit {_exitTry.Num}; me {(p0 == null ? "?" : $"{Px(p0.xof):0},{Px(p0.yof):0}")}");
                Speech.Say("Couldn't get through the exit. Try pressing Space here, or walking with W A S D.", true, false);
                _exitTry = null; return;
            }
            int k = ExitDirs[_exitTryStep];
            var p = g.GetMyCharBuff();
            Main.Log.Msg($"Nav: still in the room; trying keys {k} from {(p == null ? "?" : $"{Px(p.xof):0},{Px(p.yof):0}")}");
            SetKeys(k);
            _facing = true; _faceUntil = Time.unscaledTime + 0.8f;
        }

        private static void StopWalk(string say)
        {
            _walking = false; _exitTry = null;
            SetKeys(0);
            if (!string.IsNullOrEmpty(say)) Speech.Say(say, true, false);
        }

        // ---------- Key presses ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

        // The game's up / left / down / right keys (W A S D unless the player changed them), by scan code.
        private static readonly Il2CppGK_System.Controller.ButtonType[] Dir =
        {
            Il2CppGK_System.Controller.ButtonType.DPAD_Up, Il2CppGK_System.Controller.ButtonType.DPAD_Left,
            Il2CppGK_System.Controller.ButtonType.DPAD_Down, Il2CppGK_System.Controller.ButtonType.DPAD_Right,
        };
        private static readonly int[] DefaultScan = { 0x11, 0x1E, 0x1F, 0x20 };   // W A S D
        private static readonly int[] _sentScan = new int[4];   // what was pressed, so the same key is let go

        private static INPUT Key(int scan, bool down)
        {
            uint flags = down ? 0u : 2u;              // KEYEVENTF_KEYUP
            if ((scan & 0xFF00) == 0xE000) flags |= 1u;  // KEYEVENTF_EXTENDEDKEY
            return new INPUT { type = 1, ki = new KEYBDINPUT { wVk = (ushort)KeyMap.VkOfScan(scan), wScan = (ushort)(scan & 0xFF), dwFlags = flags } };
        }

        private static void SetKeys(int keys)
        {
            if (keys == _keysDown) return;
            var list = new List<INPUT>();
            for (int i = 0; i < 4; i++)
            {
                bool want = (keys & (1 << i)) != 0, has = (_keysDown & (1 << i)) != 0;
                if (want == has) continue;
                if (want)
                {
                    int sc = 0; try { sc = KeyMap.Scan(Dir[i]); } catch { }
                    _sentScan[i] = sc != 0 ? sc : DefaultScan[i];
                }
                list.Add(Key(_sentScan[i] != 0 ? _sentScan[i] : DefaultScan[i], want));
            }
            _keysDown = keys;
            if (list.Count > 0) SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
        }

        /// <summary>Hold the up / left / down / right keys for other screens (bits: 1 up, 2 left, 4 down, 8 right; 0 releases).</summary>
        public static void HoldKeys(int keys) => SetKeys(keys);

        /// <summary>True while the mod itself is holding walk keys (so prompts aren't held back for it).</summary>
        public static bool HoldingKeys => _keysDown != 0;
    }

    /// <summary>Readable names for the game's object numbers (GK_System.AutoPlay.GK1.OBJ_ID).</summary>
    internal static class ObjNames
    {
        private static readonly Dictionary<string, string> People = new Dictionary<string, string>
        {
            { "MITSURUGI", "Edgeworth" }, { "MITSURUGI2", "Edgeworth" }, { "OLD_MITSURUGI", "young Edgeworth" },
            { "ITONOKO", "Gumshoe" }, { "OLD_ITONOKO", "young Gumshoe" },
            { "MIKUMO", "Kay" }, { "OLD_MIKUMO", "young Kay" },
            { "KARUMAMEI", "Franziska" }, { "OLD_KARUMAMEI", "young Franziska" },
            { "ROW", "Lang" }, { "OLD_ROW", "young Lang" }, { "SHENA", "Shih-na" },
            { "BADHO", "Badd" }, { "OLD_BADHO", "young Badd" },
            { "POLICE", "police officer" }, { "POLICE2", "police officer" }, { "POLICE3", "police officer" }, { "POLICE4", "police officer" },
            { "KANSHIKI1", "forensics officer" }, { "KANSHIKI2", "forensics officer" },
            { "DETECTIVE1", "detective" }, { "DETECTIVE2", "detective" }, { "DETECTIVE3", "detective" }, { "DETECTIVE4", "detective" },
            { "OLD_SAIBANCHO", "judge" }, { "CAPTAIN", "captain" }, { "REPORTER", "reporter" }, { "CAMERAMAN", "cameraman" },
            { "GUARD1", "guard" }, { "GUARD2", "guard" }, { "SUZUKI", "Maggey" }, { "S0_INIT", "Maggey" }, { "JYOICHIRO", "Ernest" }, { "HARABAI", "Meekins" }, { "TAIHOKUN_H", "Blue Badger" }, { "TAIHOCHAN_H", "Pink Badger" },
        };

        private static readonly Dictionary<string, string> Words = new Dictionary<string, string>
        {
            { "L", "left" }, { "R", "right" }, { "UL", "upper left" }, { "UC", "upper middle" }, { "UR", "upper right" },
            { "DL", "lower left" }, { "DC", "lower middle" }, { "DR", "lower right" }, { "N", "" }, { "F", "front" },
            { "UP", "" }, { "DW", "fallen" }, { "MOB1", "person" }, { "MOB2", "person" }, { "MOB3", "person" }, { "MOB4", "person" }, { "MOB5", "person" },
            { "LIHGT01", "light 1" }, { "LIHGT02", "light 2" }, { "LIHGT03", "light 3" }, { "LIHGT04", "light 4" },
            { "TEASET", "tea set" }, { "CORPSE", "body" }, { "CORPSE2", "body" }, { "KEEPOUT", "keep out tape" }, { "ARIA01", "area 1" }, { "ARIA02", "area 2" },
            { "TONOSAMAN", "Steel Samurai figure" }, { "FILE1", "files" }, { "FILE2", "files" }, { "FILE3", "files" }, { "WALL", "wall" },
        };

        // Specific objects whose generic name would be confusing.
        private static readonly Dictionary<string, string> Specific = new Dictionary<string, string>
        {
            { "OF_CORPSE", "body" }, { "OF_CORPSE2", "body, upper part" }, { "OF_WALL_L", "left wall, by the jacket" }, { "OF_FILE1", "scattered files" }, { "OF_FILE2", "scattered files, right" },
            { "CRD00_DOOR_MITSURUGI", "door to your office" }, { "CRD00_DOOR_YUUKI", "office door" }, { "CRD00_MOBPOLICE", "police officer" },
        };

        public static string Raw(int num)
        {
            try { return Enum.GetName(typeof(Il2CppGK_System.AutoPlay.GK1.OBJ_ID), num) ?? num.ToString(); } catch { return num.ToString(); }
        }

        public static bool IsPerson(int num) => Raw(num).StartsWith("CHAR_");

        /// <summary>A main character whose name the mod already knows (e.g. Franziska): never learned from conversations.</summary>
        public static bool KnownName(int num)
        {
            string raw = Raw(num);
            return raw.StartsWith("CHAR_") && People.ContainsKey(raw.Substring(5));
        }

        // Names learned from conversations (saved, so they're known next time).
        private static Dictionary<int, string> _learned;
        private static string LearnedFile => System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_names.txt");

        private static Dictionary<int, string> Learned()
        {
            if (_learned != null) return _learned;
            _learned = new Dictionary<int, string>();
            try
            {
                if (System.IO.File.Exists(LearnedFile))
                    foreach (var l in System.IO.File.ReadAllLines(LearnedFile))
                    {
                        int eq = l.IndexOf('=');
                        if (eq > 0 && int.TryParse(l.Substring(0, eq), out int n)) _learned[n] = l.Substring(eq + 1);
                    }
            }
            catch (Exception e) { Main.Log.Warning("Couldn't read learned names: " + e.Message); }
            return _learned;
        }

        public static void Learn(int num, string name)
        {
            var d = Learned();
            if (d.TryGetValue(num, out var old) && old == name) return;
            d[num] = name;
            Main.Log.Msg($"Learned name: {Raw(num)} is {name}");
            try
            {
                var lines = new List<string>();
                foreach (var kv in d) lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(LearnedFile, lines);
            }
            catch (Exception e) { Main.Log.Warning("Couldn't save learned names: " + e.Message); }
        }

        public static bool IsMainCharacterName(string name)
        {
            foreach (var v in People.Values) if (string.Equals(v, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string LearnedName(int num) => Learned().TryGetValue(num, out var n) ? n : null;

        public static void Forget(int num)
        {
            var d = Learned();
            if (!d.Remove(num)) return;
            Main.Log.Msg($"Forgot learned name of {Raw(num)}");
            try
            {
                var lines = new List<string>();
                foreach (var kv in d) lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(LearnedFile, lines);
            }
            catch (Exception e) { Main.Log.Warning("Couldn't save learned names: " + e.Message); }
        }

        /// <summary>Playing the flashback episode: the game's OLD_ characters are the young versions of people.</summary>
        public static bool Flashback;

        public static string Name(int num)
        {
            string n0 = Name0(num);
            // Outside the flashback, OLD_ just means another sprite of the same person (e.g. Badd in episode 5).
            if (!Flashback && n0.StartsWith("young ") && Raw(num).Contains("OLD_")) n0 = n0.Substring(6);
            return n0;
        }

        private static string Name0(int num)
        {
            string raw = Raw(num);
            if (KnownName(num)) return People[raw.Substring(5)];
            if (Learned().TryGetValue(num, out var ln)) return ln;
            if (Specific.TryGetValue(raw, out var sp)) return sp;
            if (raw.StartsWith("CHAR_"))
            {
                string n = raw.Substring(5);
                if (People.TryGetValue(n, out var p)) return p;
                if (n.StartsWith("OLD_")) return "young " + Pretty(n.Substring(4));
                return Pretty(n);
            }
            int us = raw.IndexOf('_');
            if (us < 0 || raw.StartsWith("MZ")) return "spot " + num;
            var parts = raw.Substring(us + 1).Split('_');
            var outp = new List<string>();
            foreach (var w in parts)
            {
                // The game numbers its variants of one thing (FIREPLACE / FIREPLACE2): that's not part of the name.
                string wd = w.Length > 1 && char.IsLetter(w[0]) ? w.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') : w;
                if (Words.TryGetValue(wd, out var r)) { if (r.Length > 0) outp.Add(r); }
                else outp.Add(wd.ToLowerInvariant());
            }
            return outp.Count == 0 ? "spot " + num : string.Join(" ", outp);
        }

        private static string Pretty(string s)
        {
            s = s.Replace('_', ' ').ToLowerInvariant();
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
