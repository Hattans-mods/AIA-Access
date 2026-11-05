extern alias GK1;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AAIAccess
{
    /// <summary>
    /// Development tool (F9): writes every evidence "Check" page picture (first game) to UserData/AAIAccess_pages,
    /// with the list of which pages belong to which detail, so their text can be read out offline.
    /// </summary>
    internal static class PageExport
    {
        private static Dictionary<uint, string> _ncgNames;
        private static readonly Queue<string> _todo = new Queue<string>();
        private static string Dir => System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_pages");

        /// <summary>The picture's name (e.g. upcut_02_42) for a picture number, from the game's own list of constants.</summary>
        public static string NcgName(uint id)
        {
            if (_ncgNames == null)
            {
                _ncgNames = new Dictionary<uint, string>();
                // Picture numbers and names (from the game's constants, which aren't visible at run time).
                try
                {
                    using var st = typeof(PageExport).Assembly.GetManifestResourceStream("ncgnames.txt");
                    using var rd = new System.IO.StreamReader(st);
                    string l;
                    while ((l = rd.ReadLine()) != null)
                    {
                        int eq = l.IndexOf('=');
                        if (eq > 0 && uint.TryParse(l.Substring(0, eq), out uint u)) _ncgNames[u] = l.Substring(eq + 1).Trim();
                    }
                }
                catch (Exception e) { Main.Log.Warning("NCG names: " + e.Message); }
                Main.Log.Msg($"{_ncgNames.Count} picture names known");
            }
            return _ncgNames.TryGetValue(id, out var n) ? n : null;
        }

        private static IEnumerable<string> AllNames() { NcgName(0); return new List<string>(_ncgNames.Values); }

        public static void Start()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Dir);
                var tbl = GK1::Il2Cpp.ItemListMenu.ReadOnly.DetailListTbl;
                var names = new HashSet<string>();
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < tbl.Length; i++)
                {
                    var d = tbl[i]; if (d == null) continue;
                    var files = d.files;
                    var line = new List<string>();
                    for (int k = 0; files != null && k < files.Length; k++)
                    {
                        var fc = files[k]; if (fc == null) continue;
                        string n = d.detail_type == 3 ? null : NcgName((uint)fc.NCG);
                        line.Add($"{fc.f1:X}/{fc.f2:X}/{fc.f3:X} ncg={fc.NCG} imd={fc.IMD} -> {n}");
                        if (n != null) names.Add(n);
                    }
                    sb.AppendLine($"detail {i} ({(Il2CppGK_System.AutoPlay.GK1.DETAIL_ID)i}) type={d.detail_type}: {string.Join(" | ", line)}");
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(Dir, "details.txt"), sb.ToString());
                Main.Log.Msg("Detail table:\n" + sb);
                // Every other upcut picture too (close-ups), for describing them.
                foreach (var kv in AllNames()) if (kv.StartsWith("upcut")) names.Add(kv);
                int skipped = 0;
                foreach (var n in names)
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(Dir, n + "_eng.png")) || System.IO.File.Exists(System.IO.Path.Combine(Dir, n + ".png"))) { skipped++; continue; }
                    _todo.Enqueue(n);
                }
                Main.Log.Msg($"Export: {_todo.Count} to do, {skipped} already there");
                Speech.Say(_todo.Count == 0 ? "All pictures are already exported." : $"Exporting {_todo.Count} pictures.", true, false);
            }
            catch (Exception e) { Main.Log.Error("Page export failed: " + e); Speech.Say("Export failed.", true, false); }
        }

        public static void Poll()
        {
            if (_todo.Count == 0) return;
            string n = _todo.Dequeue();
            foreach (var addr in new[] { $"GK1/Ipnce/FHD/Texture/{n}_eng.png", $"GK1/Ipnce/FHD/Texture/{n}.png" })
            {
                try
                {
                    if (!Il2CppGK_System.Resource.ResourceManager.AddressIsExist(addr)) continue;
                    var res = Il2CppGK_System.Resource.ResourceManager.Load<Texture2D>(addr, Il2CppGK_System.Resource.LocalizeMode.None, false);
                    var tex = res?.Asset;
                    if (tex == null) { Main.Log.Msg($"Couldn't load {addr}"); continue; }
                    int w = tex.width, h = tex.height;
                    var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(tex, rt);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var t2 = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    t2.ReadPixels(new Rect(0, 0, w, h), 0, 0); t2.Apply();
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    byte[] png = ImageConversion.EncodeToPNG(t2);
                    string file = System.IO.Path.GetFileName(addr);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(Dir, file), png);
                    UnityEngine.Object.Destroy(t2);
                    try { res.Release(); } catch { }
                    Main.Log.Msg($"Exported {file} ({w}x{h})");
                    break;
                }
                catch (Exception e) { Main.Log.Warning($"Export {addr}: {e.Message}"); }
            }
            if (_todo.Count == 0) Speech.Say("Export finished.", true, false);
        }
    }
}
