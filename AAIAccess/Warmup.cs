extern alias GK2;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using G2 = GK2::Il2Cpp;

namespace AAIAccess
{
    /// <summary>The first use of each game class from the mod sets it up (looking up all its methods and fields in the game),
    /// which for the second game's big classes took about 3 seconds, felt as a freeze on the first close-up after loading.
    /// Do that set-up once while the game starts instead, for the classes the mod uses and the ones they hand back.</summary>
    internal static class Warmup
    {
        private static bool _done;

        public static void Poll()
        {
            if (_done || UnityEngine.Time.unscaledTime < 2f) return;
            _done = true;
            var total = System.Diagnostics.Stopwatch.StartNew();
            var seeds = new[]
            {
                typeof(G2.GKJ2Global), typeof(G2.cUpcutCtrl), typeof(G2.cSubscrnTaskCheckCursor), typeof(G2.cSubscrnTaskCheckMoveButton),
                typeof(G2.cSubscrnCtrl), typeof(G2.cSceneMovieMG), typeof(G2.cSceneLuminol), typeof(G2.cSceneFingerPrint), typeof(G2.cSceneItem3D), typeof(G2.cItem3DModel), typeof(G2.cItem3DTaskCursor),
                typeof(G2.cSceneItem2D), typeof(G2.cScenario), typeof(G2.cHpGauge), typeof(G2.cMapCtrl), typeof(G2.cMapChar), typeof(G2.cMapColl),
                typeof(G2.cFx32), typeof(G2.cVector2D), typeof(G2.cQuaternion), typeof(G2.cMapTask),
            };
            var asm = typeof(G2.GKJ2Global).Assembly;
            var seen = new HashSet<Type>(); var queue = new Queue<(Type t, int depth)>();
            foreach (var t in seeds) if (seen.Add(t)) queue.Enqueue((t, 0));
            var slow = new List<string>();
            while (queue.Count > 0)
            {
                var (t, depth) = queue.Dequeue();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { RuntimeHelpers.RunClassConstructor(t.TypeHandle); } catch { }
                if (sw.ElapsedMilliseconds >= 30) slow.Add($"{t.Name} {sw.ElapsedMilliseconds} ms");
                if (depth >= 2) continue;
                void Add(Type x)
                {
                    if (x == null) return;
                    if (x.IsGenericType) foreach (var g in x.GetGenericArguments()) Add(g);
                    if (x.IsArray) x = x.GetElementType();
                    if (x.Assembly == asm || (x.IsGenericType && x.Namespace != null && x.Namespace.StartsWith("Il2Cpp")))
                        if (seen.Add(x)) queue.Enqueue((x, depth + 1));
                }
                try
                {
                    const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                    foreach (var m in t.GetMethods(all)) { Add(m.ReturnType); foreach (var p in m.GetParameters()) Add(p.ParameterType); }
                    foreach (var p in t.GetProperties(all)) Add(p.PropertyType);
                    foreach (var n in t.GetNestedTypes(all)) Add(n);
                }
                catch { }
            }
            Main.Log.Msg($"Warm-up: set up {seen.Count} game classes in {total.ElapsedMilliseconds} ms" + (slow.Count > 0 ? "; slow: " + string.Join(", ", slow) : ""));
        }
    }
}
