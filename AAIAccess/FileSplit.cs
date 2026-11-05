#if DEVTOOLS
using System;
using System.IO;
using System.Threading;

namespace AAIAccess
{
    /// <summary>Developer helper: if UserData/AAIAccess_split.txt names a game file (relative to the game folder), copy it
    /// into UserData/AAIAccess_parts/ in pieces of 200 MB so it can be picked up for study. Runs once in the background.</summary>
    internal static class FileSplit
    {
        public static void Start()
        {
            string ud = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
            string cfg = Path.Combine(ud, "AAIAccess_split.txt");
            if (!File.Exists(cfg)) return;
            new Thread(() =>
            {
                try
                {
                    string game = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
                    string outDir = Path.Combine(ud, "AAIAccess_parts");
                    Directory.CreateDirectory(outDir);
                    foreach (var raw in File.ReadAllLines(cfg))
                    {
                        string rel = raw.Trim(); if (rel.Length == 0 || rel.StartsWith("#")) continue;
                        string src = Path.Combine(game, rel);
                        if (!File.Exists(src)) { Main.Log.Warning("Split: missing " + src); continue; }
                        const long Part = 200L * 1024 * 1024;
                        var buf = new byte[1 << 20];
                        using var fs = File.OpenRead(src);
                        int n = 0;
                        while (fs.Position < fs.Length)
                        {
                            string dst = Path.Combine(outDir, Path.GetFileName(src) + ".part" + n++);
                            using var o = File.Create(dst);
                            long left = Part;
                            while (left > 0)
                            {
                                int r = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
                                if (r <= 0) break;
                                o.Write(buf, 0, r); left -= r;
                            }
                        }
                        Main.Log.Msg($"Split {rel} into {n} parts");
                    }
                    File.Delete(cfg);
                }
                catch (Exception e) { Main.Log.Warning("Split failed: " + e.Message); }
            }) { IsBackground = true }.Start();
        }
    }
}
#endif
