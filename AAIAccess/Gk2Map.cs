extern alias GK2;
using System;
using System.Text;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>Second game (Prosecutor's Gambit): its own map engine. Step 1: read and log the map data.</summary>
    internal static class Gk2Map
    {
        public static G2.cMapCtrl Ctrl
        {
            get { try { return G2.cMapCtrl.Instance; } catch { return null; } }
        }

        /// <summary>The second game is being played (its scenario exists), even when no map is loaded (e.g. a save made on a close-up).</summary>
        public static bool InGame
        {
            get { try { return G2.cMapCtrl.Instance != null || G2.cScenario.Instance != null; } catch { return false; } }
        }

        public static float Fx(G2.cFx32 v) => v == null ? float.NaN : v.mVal / 4096f;
        public static string V2(G2.cVector2D v) => $"{Fx(v.x):0.#},{Fx(v.y):0.#}";
        public static string V3(G2.GKJ2Global.cVector3D v) => v == null ? "-" : $"{Fx(v.x):0.#},{Fx(v.y):0.#},{Fx(v.z):0.#}";

        public static G2.cUpcutCtrl Upcut
        {
            get { try { return G2.cUpcutCtrl.Instance; } catch { return null; } }
        }

        /// <summary>Is a close-up picture being examined with the cursor right now?</summary>
        public static bool UpcutCursorOn
        {
            get { try { var u = Upcut; var c = u?.cursor(); return c != null && c.isEnable(); } catch { return false; } }
        }

        /// <summary>The close-up screen is up when a picture is loaded and the game is in one of its upcut modes.</summary>
        public static bool UpcutShown
        {
            get
            {
                try
                {
                    var u = Upcut; if (u == null) return false;
                    var m = u.getUpcutMode().ToString();
                    return m.Contains("CHECK") && !m.EndsWith("NONE");
                }
                catch { return false; }
            }
        }

        private static void Shot(string tag)
        {
            try
            {
                string f = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_" + tag + ".png");
                UnityEngine.ScreenCapture.CaptureScreenshot(f);
                Main.Log.Msg($"Screenshot: {f} (screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height})");
            }
            catch (Exception e) { Main.Log.Msg("Screenshot failed: " + e.Message); }
        }

        private static void DumpUpcut(StringBuilder sb)
        {
            var u = Upcut; if (u == null) return;
            try
            {
                var bg = u.bg();
                sb.AppendLine($"UPCUT mode={u.getUpcutMode()} id={bg?.getUpcutID()} type={bg?.getUpcutType()} pos={(bg == null ? "-" : V2(bg.pos()))} target={(bg == null ? "-" : V2(bg.getTargetPos()))} adjW={bg?.GetBGAdjustWidth()}");
                var cur = u.cursor();
                if (cur != null) sb.AppendLine($"CURSOR enable={cur.isEnable()} pos={V2(cur.pos())} mPos={V2(cur.mPos)} scr={u.GetUpcutToScrnX(cur.pos())},{u.GetUpcutToScrnY(cur.pos())}");
                var co = u.coll();
                if (co != null)
                {
                    var chk = co.getCheckCollTbl(); int nc = co.getCheckCollNum();
                    for (int i = 0; i < nc && chk != null && i < chk.Length; i++)
                    {
                        var c = chk[i]; if (c == null) continue;
                        sb.AppendLine($"UCHECK[{i}] flag={c.RefFlagNo} shape={c.Shape} {V2(c.Start)} - {V2(c.End)} label={c.ScriptLabelNo} on={SafeOn(co, c.RefFlagNo)} scr={u.GetUpcutToScrnX(c.Start)},{u.GetUpcutToScrnY(c.Start)}-{u.GetUpcutToScrnX(c.End)},{u.GetUpcutToScrnY(c.End)}");
                    }
                }
            }
            catch (Exception e) { sb.AppendLine("upcut: " + e.Message); }
        }

        /// <summary>F10 in the second game: everything about the current map, for working out how it fits together.</summary>
        public static bool Dump()
        {
            var mc = Ctrl;
            if (mc == null) return false;
            {
                var u0 = Upcut;
                string why = u0 == null ? "no upcut instance" : "";
                if (u0 != null) try { why = $"mode={u0.getUpcutMode()} cursor={(u0.cursor() == null ? "null" : u0.cursor().isEnable().ToString())}"; } catch (Exception e) { why = e.Message; }
                Main.Log.Msg("Upcut state: " + why);
            }
            if (UpcutCursorOn || UpcutShown)
            {
                var ub = new StringBuilder("==== GK2 UPCUT DUMP ====\n");
                DumpUpcut(ub);
                Main.Log.Msg(ub.ToString());
                Shot("upcut");
                Speech.Say("Picture saved to log.", true, false);
                return true;
            }
            var sb = new StringBuilder();
            sb.AppendLine("==== GK2 MAP DUMP ====");
            try { sb.AppendLine($"MAP id={mc.getMapID()} validMove={mc.getValidMoveKeyFlag()}"); } catch (Exception e) { sb.AppendLine("map id: " + e.Message); }
            var tc = mc.taskCtrl();
            try
            {
                var pl = tc?.getPlayer();
                if (pl != null) sb.AppendLine($"PLAYER res={pl.getResID()} name={pl.getNameID()} pos={V3(pl.pos())} scr={mc.getMapToScrnX(pl.pos())},{mc.getMapToScrnY(pl.pos())} dir={pl.dir()} coll={pl.coll()} check={V3(pl.check_pos)} control={pl.getControl()}");
                var pa = tc?.getPartner();
                if (pa != null) sb.AppendLine($"PARTNER res={pa.getResID()} name={pa.getNameID()} pos={V3(pa.pos())}");
            }
            catch (Exception e) { sb.AppendLine("player: " + e.Message); }
            try
            {
                int n = 0;
                for (var t = tc?.getFirst(); t != null && n < 200; t = tc.getNext(t), n++)
                {
                    var mt = t.TryCast<G2.cMapTask>();
                    if (mt == null) { sb.AppendLine($"TASK {t.getTaskID()} (not a map task)"); continue; }
                    string extra = "";
                    var ch = t.TryCast<G2.cMapChar>();
                    if (ch != null) { try { extra = $" res={ch.getResID()} noTalk={ch.isNoTalk()}"; } catch { } }
                    sb.AppendLine($"TASK {t.getTaskID()} type={mt.getType()} name={mt.getNameID()} pos={V3(mt.pos())} scr={mc.getMapToScrnX(mt.pos())},{mc.getMapToScrnY(mt.pos())} dir={mt.dir()} coll={mt.coll()} invisible={mt.isInvisible()}{extra}");
                }
            }
            catch (Exception e) { sb.AppendLine("tasks: " + e.Message); }
            try
            {
                var co = mc.coll();
                var chk = co.getCheckCollTbl(); int nc = co.getCheckCollNum();
                for (int i = 0; i < nc && chk != null && i < chk.Length; i++)
                {
                    var c = chk[i]; if (c == null) continue;
                    sb.AppendLine($"CHECK[{i}] type={c.Type} flag={c.RefFlagNo} shape={c.Shape} {V2(c.Start)} - {V2(c.End)} label={c.ScriptLabelNo} foot={c.FootHit} on={SafeOn(co, c.RefFlagNo)} scr={mc.getMapToScrnX(c.Start)},{mc.getMapToScrnY(c.Start)}-{mc.getMapToScrnX(c.End)},{mc.getMapToScrnY(c.End)}");
                }
                var dr = co.getDoorCollTbl(); int nd = co.getDoorCollNum();
                for (int i = 0; i < nd && dr != null && i < dr.Length; i++)
                {
                    var d = dr[i]; if (d == null) continue;
                    sb.AppendLine($"DOOR[{i}] type={d.Type} flag={d.RefFlagNo} shape={d.Shape} {V2(d.Start)} - {V2(d.End)} move={d.MoveTableNo} arrow={d.ArrowDispX},{d.ArrowDispY} {d.ArrowDispDir} on={SafeOn(co, d.RefFlagNo)}");
                }
                var ad = co.getAdjustCollTbl(); int na = co.getAdjustCollNum();
                for (int i = 0; i < na && ad != null && i < ad.Length; i++)
                {
                    var a = ad[i]; if (a == null) continue;
                    sb.AppendLine($"ADJUST[{i}] type={a.Type} flag={a.RefFlagNo} r={Fx(a.Radius):0.#} {V2(a.Start)} - {V2(a.End)} centre={V2(a.Center)} on={SafeOn(co, a.RefFlagNo)}");
                }
                var tr = co.getTraceCollTbl(); int nt = co.getTraceCollNum();
                for (int i = 0; i < nt && tr != null && i < tr.Length; i++)
                {
                    var x = tr[i]; if (x == null) continue;
                    string links = ""; try { var l = x.Link; if (l != null) for (int k = 0; k < l.Length; k++) links += l[k] + " "; } catch { }
                    sb.AppendLine($"TRACE[{i}] type={x.Type} flag={x.RefFlagNo} pos={V2(x.Pos)} links=[{links.Trim()}]");
                }
            }
            catch (Exception e) { sb.AppendLine("coll: " + e.Message); }
            sb.AppendLine("==== END GK2 MAP ====");
            Shot("map");
            Main.Log.Msg(sb.ToString());
            Speech.Say("Map saved to log.", true, false);
            return true;
        }

        private static string SafeOn(G2.cMapColl co, uint flag) { try { return co.isCollEnable(flag).ToString(); } catch { return "?"; } }
    }
}
