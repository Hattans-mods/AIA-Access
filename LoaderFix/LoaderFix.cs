using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(AAIAccessLoaderFix.Plugin), "AAI Access Loader Fix", "2.0.0", "Hattans-mods")]

namespace AAIAccessLoaderFix
{
    // MelonLoader's first-time build of its helper files stops with
    // "IOException: Attempt to access invalid address" while closing a read-only view of
    // GameAssembly.dll (Il2CppInterop Pass16ScanMethodRefs). The scan itself has finished by then,
    // so this plugin lets the build carry on when that one harmless error happens.
    public class Plugin : MelonPlugin
    {
        private static string _dump;
        private static bool _genPatched;
        private static Assembly _gen;   // MelonLoader's builder module, once loaded

        /// <summary>The game is running now, with its real code in memory: make sure there is a copy of it on disk
        /// before MelonLoader starts building.</summary>
        public override void OnApplicationEarlyStart()
        {
            _dump = CodeDump.Ensure(MelonLoader.Utils.MelonEnvironment.GameRootDirectory);
        }

        /// <summary>MelonLoader's builder (a module it loads later): point it at the copy of the game's code.</summary>
        private static void PatchGenerator(Assembly asm)
        {
            if (_genPatched || asm.GetName().Name != "Il2CppAssemblyGenerator") return;
            _genPatched = true; _gen = asm;
            try
            {
                var h = new HarmonyLib.Harmony("AAIAccess.LoaderFix.Generator");
                var core = asm.GetType("MelonLoader.Il2CppAssemblyGenerator.Core");
                var init = core?.GetMethod("OnInitialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (init != null) h.Patch(init, postfix: new HarmonyMethod(typeof(Plugin), nameof(CoreInitPostfix)));
                var pkg = asm.GetType("MelonLoader.Il2CppAssemblyGenerator.Packages.Models.ExecutablePackage");
                var exec = pkg == null ? null : AccessTools.Method(pkg, "Execute", new[] { typeof(string[]), typeof(bool), typeof(System.Collections.Generic.Dictionary<string, string>) });
                if (exec != null) h.Patch(exec, prefix: new HarmonyMethod(typeof(Plugin), nameof(ExecutePrefix)));
                MelonLogger.Msg($"Builder patched (core {init != null}, tools {exec != null}).");
            }
            catch (Exception e) { MelonLogger.Warning("Could not patch the builder: " + e.Message); }
        }

        private static void CoreInitPostfix()
        {
            if (_dump == null) return;
            try
            {
                // Look the type up in the builder itself: searching every loaded assembly by name would load MelonLoader's
                // old helper files, and then it can't delete them before writing new ones.
                var f = _gen?.GetType("MelonLoader.Il2CppAssemblyGenerator.Core")?.GetField("GameAssemblyPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                f?.SetValue(null, _dump);
                MelonLogger.Msg("The builder will read the game's code from " + CodeDump.DumpName + ".");
            }
            catch (Exception e) { MelonLogger.Warning("Could not redirect the builder: " + e.Message); }
        }

        /// <summary>Cpp2IL runs as its own program and looks for GameAssembly.dll in the game folder: tell it the files
        /// to use instead (its "force" options).</summary>
        private static void ExecutePrefix(object __instance, ref string[] args)
        {
            try
            {
                if (_dump == null || __instance == null || !__instance.GetType().Name.StartsWith("Cpp2IL")) return;
                if (Array.Exists(args, a => a == "--force-binary-path")) return;
                string gameDir = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
                string meta = Path.Combine(gameDir, "GK12_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
                string ver = "2022.3.17f1";
                try
                {
                    var uih = typeof(MelonLogger).Assembly.GetType("MelonLoader.InternalUtils.UnityInformationHandler");
                    var v = uih?.GetProperty("EngineVersion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)?.ToString();
                    if (!string.IsNullOrEmpty(v) && char.IsDigit(v[0])) ver = v;
                }
                catch { }
                var list = new System.Collections.Generic.List<string>(args)
                {
                    "--force-binary-path", "\"" + _dump + "\"",
                    "--force-metadata-path", "\"" + meta + "\"",
                    "--force-unity-version", ver,
                };
                args = list.ToArray();
                MelonLogger.Msg($"Cpp2IL will read {CodeDump.DumpName} (Unity {ver}).");
            }
            catch (Exception e) { MelonLogger.Warning("Could not redirect Cpp2IL: " + e.Message); }
        }

        public override void OnPreInitialization()
        {
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => { try { PatchGenerator(e.LoadedAssembly); } catch { } };
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) PatchGenerator(a);
            try
            {
                Assembly gen = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Il2CppInterop.Generator")
                               ?? Assembly.Load("Il2CppInterop.Generator");
                var t = gen.GetType("Il2CppInterop.Generator.Passes.Pass16ScanMethodRefs");
                var m = t?.GetMethod("DoPass", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) { LoggerInstance.Warning("Pass16ScanMethodRefs.DoPass not found; nothing to fix."); return; }
                new HarmonyLib.Harmony("AAIAccess.LoaderFix").Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(Prefix)),
                    finalizer: new HarmonyMethod(typeof(Plugin), nameof(Finalizer)));
                LoggerInstance.Msg("Ready.");
            }
            catch (Exception e) { LoggerInstance.Warning("Could not apply loader fix: " + e.Message); }
        }

        // The cause: the game keeps its code inside GK12.exe and fakes a GameAssembly.dll for itself (a virtual file
        // system inside the game's process). MelonLoader's build runs inside that process, so when it maps
        // GameAssembly.dll it gets the game's fake copy, and closing that mapping fails. The code copying tool also
        // saves the same code as GK12CodeDump.dll, a name the game doesn't fake: read that one instead.
        private static void Prefix(object[] __args)
        {
            try
            {
                foreach (var a in __args)
                {
                    if (a == null) continue;
                    var p = a.GetType().GetProperty("GameAssemblyPath");
                    if (p == null) continue;
                    string path = p.GetValue(a) as string;
                    if (string.IsNullOrEmpty(path)) continue;
                    string dump = Path.Combine(Path.GetDirectoryName(path) ?? "", "GK12CodeDump.dll");
                    if (File.Exists(dump) && !string.Equals(path, dump, StringComparison.OrdinalIgnoreCase))
                    {
                        p.SetValue(a, dump);
                        MelonLogger.Msg("Reading the game's code from GK12CodeDump.dll instead of GameAssembly.dll.");
                    }
                }
            }
            catch (Exception e) { MelonLogger.Warning("Could not redirect GameAssembly.dll: " + e.Message); }
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception is IOException io && io.StackTrace != null && io.StackTrace.Contains("MemoryMapped"))
            {
                MelonLogger.Msg("Ignored a harmless error while closing GameAssembly.dll: " + io.Message);
                return null;
            }
            return __exception;
        }
    }
}
