#if DEVTOOLS
extern alias GK1;
using System;
using System.Text;
using UnityEngine;
using G1 = GK1::Il2Cpp;

namespace AAIAccess
{
    /// <summary>F8 (developer build), first game: writes what the game's scripts, per-frame tasks and sprite
    /// animations are doing to the log, to find what the game is waiting for when it stops responding.
    /// Script commands are written as numbers: A(4000xxxx) calls the function GKJ1.FUNC_... with that number,
    /// 0(n) waits n frames; "comtbl" is the script's number (the ..._DM constants).</summary>
    internal static class Gk1State
    {
        public static void Dump()
        {
            G1.GKJ1 g = null;
            try { g = G1.AppCore.gkj1; } catch { }
            if (g == null || Gk2Map.InGame) { Speech.Say("Only in the first game.", true, false); return; }
            Main.Log.Msg("---- Game state (F8) ----");
            try
            {
                var tb = G1.GKJ1.TouchBtn;
                var s = g.GetGameFlagData()?.System;
                Main.Log.Msg($"State: buttons flg={tb.GetButtonFlg()} push={tb.GetButtonPush()} hp={ExamineScreen.Hp(g)} infer={g.IsInferMode()} mode={s?.mode} back={s?.back} submode={s?.submode}");
            }
            catch (Exception e) { Main.Log.Msg("State: " + e.Message); }
            try
            {
                var dws = G1.GKJ1.demow;
                for (int i = 0; dws != null && i < dws.Length; i++)
                {
                    var d = dws[i];
                    if (d == null || d.sts == 0) continue;
                    var list = d.com_list;
                    int n = list == null ? 0 : list.Count, at = d.com_exec_i;
                    var sb = new StringBuilder($"State: script {i}: sts={d.sts} comtbl={d.comtbl} zero={d.zero} ans={d.ans} label={d.current_label_id} at={at}/{n}:");
                    for (int j = Math.Max(0, at - 6); j < n && j <= at + 4; j++)
                    {
                        var c = list[j];
                        sb.Append(j == at ? " [" : " ").Append(c == null ? "null" : c.command_id.ToString("X"));
                        try
                        {
                            var p = c?.param_list;
                            if (p != null && p.Length > 0) { sb.Append('('); for (int k = 0; k < p.Length && k < 6; k++) sb.Append(k > 0 ? "," : "").Append(p[k].ToString("X")); sb.Append(')'); }
                        }
                        catch { }
                        if (j == at) sb.Append(']');
                    }
                    Main.Log.Msg(sb.ToString());
                }
            }
            catch (Exception e) { Main.Log.Msg("State: scripts: " + e.Message); }
            foreach (var (name, list) in new[] { ("vbt", g.vbt_work), ("nvbt", g.nvbt_work) })
                try
                {
                    for (int i = 0; list != null && i < list.Length; i++)
                    {
                        var v = list[i];
                        if (v == null || v.sts == 0) continue;
                        Main.Log.Msg($"State: {name} task {i}: sts={v.sts} type={v.type} cnt={v.cnt} pm={v.pm0:X},{v.pm1:X},{v.pm2:X},{v.pm3:X} function={(v.task == null ? "MISSING" : "set")}");
                    }
                }
                catch (Exception e) { Main.Log.Msg($"State: {name} tasks: " + e.Message); }
            try
            {
                int all = 0;
                var e = G1.Graphics.drawers?.GetEnumerator();
                while (e != null && e.MoveNext())
                {
                    var kv = e.Current;
                    var sd = kv.Value;
                    var cell = sd?.Drawer;
                    if (cell == null || cell.WasCollected || !cell) continue;
                    all++;
                    string type = ""; try { type = cell.GetIl2CppType().Name; } catch { }
                    Main.Log.Msg($"State: drawer {kv.Key:X}: seq={sd.Seq} oneShot={sd.IsOneShotMode} {type} shown={cell.IsDisplayed} ended={cell.IsAnimEnd} loop={cell.IsAnimLoop} paused={cell.IsAnimPaused} frame={cell.m_nowFrame:0.#} of {cell.TotalAnimFrame}");
                }
                Main.Log.Msg($"State: {all} drawers");
            }
            catch (Exception e) { Main.Log.Msg("State: drawers: " + e.Message); }
            Main.Log.Msg("---- end of game state ----");
            Speech.Say("Game state written to the log.", true, false);
        }
    }
}
#endif
