extern alias GK1;
using System;
using UnityEngine;
using G1 = GK1::Il2Cpp;

namespace AAIAccess
{
    /// <summary>First game: repairs a broken state that stops the game after loading some saves.
    /// The game runs a list of per-frame tasks (GKJ1.vbt_work, run by GKJ1.VBTexec). A save keeps each task's state
    /// and a type number, and loading finds the task's function again from that number (VBT.Load: GKJ1.GetTask(type)).
    /// The task that scrolls a close-up picture taller than the screen (GKJ1.InquiryScrollVBT) is set up by
    /// Inquiry_MoveScroll with type 0, which has no function. It stays in the list until the picture reaches the edge
    /// it is scrolling towards; set up with a direction it doesn't know (as ExamineScreen's first try at scrolling
    /// does, with 64 / 128), it stays until the close-up is over. A save made in that time (also an auto save) loads
    /// with a task that is switched on but has no function: VBTexec throws a NullReferenceException every frame and
    /// the rest of the frame is skipped. That rest includes Graphics.OnUpdate, so no sprite animation moves any more
    /// and the game hangs at the next thing that waits for one (the cut-in after Deduce, the end of a deduction...).
    /// Here: a task that is switched on but has no function is switched off. Nothing is lost by that: such a task
    /// can't run, and the scroll task is set up again the next time the picture is scrolled.</summary>
    internal static class VbtRepair
    {
        private static float _next;

        public static void Poll()
        {
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.1f;
            if (Gk2Map.InGame) return;
            try
            {
                var list = G1.AppCore.gkj1?.vbt_work;
                for (int i = 0; list != null && i < list.Length; i++)
                {
                    var v = list[i];
                    if (v == null || v.sts == 0 || v.task != null) continue;
                    Main.Log.Msg($"Loaded save had a frame task without its function (slot {i}, type {v.type}, parameter {v.pm0:X}); switched it off so the game runs again.");
                    v.Clear();
                }
            }
            catch (Exception e) { Main.Log.Warning("Frame task check: " + e.Message); }
        }
    }
}
