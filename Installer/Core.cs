using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AAIAccessInstaller
{
    /// <summary>A copy of the game found on this PC.</summary>
    internal sealed class GameDir
    {
        public string Name, Path;
        public override string ToString() => $"{Name} ({Path})";
    }

    /// <summary>The newest release on GitHub.</summary>
    internal sealed class Release
    {
        public string Tag;          // "V1.1.0"
        public Version Version;
        public string ZipName, ZipUrl;
    }

    /// <summary>Everything the installer does, without the window (so it can also run from the command line).</summary>
    internal static class Core
    {
        public const string Repo = "Hattans-mods/AIA-Access";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        public const string DotNet6Page = "https://dotnet.microsoft.com/download/dotnet/6.0";
        private static readonly (string id, string name)[] SteamApps =
        {
            ("2401970", "Ace Attorney Investigations Collection"),
            ("2728410", "Ace Attorney Investigations Collection demo"),
        };

        public static Action<string> Log = _ => { };

        // ---------- finding the game ----------

        /// <summary>Copies of the game in the player's Steam libraries.</summary>
        public static List<GameDir> FindGames()
        {
            var found = new List<GameDir>();
            var libs = new List<string>();
            foreach (var steam in SteamRoots())
            {
                libs.Add(steam);
                string vdf = System.IO.Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                try
                {
                    if (File.Exists(vdf))
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                catch (Exception e) { Log("Steam library list: " + e.Message); }
            }
            foreach (var lib in libs.Select(Norm).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var (id, name) in SteamApps)
                {
                    try
                    {
                        string acf = System.IO.Path.Combine(lib, "steamapps", "appmanifest_" + id + ".acf");
                        if (!File.Exists(acf)) continue;
                        var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"");
                        if (!m.Success) continue;
                        string dir = System.IO.Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
                        if (IsGameDir(dir) && !found.Any(g => string.Equals(g.Path, dir, StringComparison.OrdinalIgnoreCase)))
                            found.Add(new GameDir { Name = name, Path = dir });
                    }
                    catch (Exception e) { Log("Steam app " + id + ": " + e.Message); }
                }
            }
            return found;
        }

        private static IEnumerable<string> SteamRoots()
        {
            var roots = new List<string>();
            void Add(string p) { if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) roots.Add(Norm(p)); }
            try { Add(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string); } catch { }
            try { Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string); } catch { }
            try { Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string); } catch { }
            Add(@"C:\Program Files (x86)\Steam");
            return roots.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string Norm(string p) => System.IO.Path.GetFullPath(p.Replace('/', '\\')).TrimEnd('\\');

        /// <summary>The folder holds the game (GK12.exe).</summary>
        public static bool IsGameDir(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(System.IO.Path.Combine(dir, "GK12.exe"));

        // ---------- versions ----------

        /// <summary>The installed mod's version, null when it isn't installed. Old versions without a version number
        /// still name it in their mod information ("AAI Access", "1.0.0", ...).</summary>
        public static Version InstalledVersion(string dir, out bool installed)
        {
            string dll = System.IO.Path.Combine(dir, "Mods", "AAIAccess.dll");
            installed = File.Exists(dll);
            if (!installed) return null;
            try
            {
                var v = System.Reflection.AssemblyName.GetAssemblyName(dll).Version;
                if (v != null && v > new Version(0, 0, 0, 0)) return Trim(v);
            }
            catch { }
            try
            {
                // MelonInfo("...Main", "AAI Access", "1.0.1", "Hattans-mods"): length-prefixed strings in the attribute blob.
                byte[] b = File.ReadAllBytes(dll);
                byte[] key = Encoding.UTF8.GetBytes("AAI Access");
                for (int i = 1; i + key.Length + 1 < b.Length; i++)
                {
                    if (b[i - 1] != key.Length) continue;
                    bool hit = true;
                    for (int k = 0; k < key.Length && hit; k++) hit = b[i + k] == key[k];
                    if (!hit) continue;
                    int at = i + key.Length, n = b[at];
                    if (n <= 0 || n > 20 || at + 1 + n > b.Length) continue;
                    // Release 1.0.0 called itself 0.1.0 by mistake (fixed in 1.0.1).
                    if (Version.TryParse(Encoding.UTF8.GetString(b, at + 1, n), out var v)) return Trim(v) == new Version(0, 1, 0) ? new Version(1, 0, 0) : Trim(v);
                }
            }
            catch { }
            return new Version(0, 0);   // installed, version unknown: older than any release
        }

        public static Version ParseTag(string tag)
        {
            var m = Regex.Match(tag ?? "", @"(\d+(?:\.\d+){0,3})");
            return m.Success && Version.TryParse(m.Groups[1].Value.Contains('.') ? m.Groups[1].Value : m.Groups[1].Value + ".0", out var v) ? Trim(v) : null;
        }

        /// <summary>1.1.0.0 and 1.1 both become 1.1.0, so versions compare the same however they were written.</summary>
        public static Version Trim(Version v) => new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

        public static string Show(Version v) => v == null ? "" : v.Major == 0 && v.Minor == 0 ? "an older version" : v.ToString(3);

        // ---------- GitHub ----------

        private static HttpClient Http()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("AAIAccess-Installer");   // GitHub requires one; nothing about the player
            return h;
        }

        /// <summary>The newest release, from GitHub's public release list.</summary>
        public static async Task<Release> LatestRelease()
        {
            using (var h = Http())
            {
                h.Timeout = TimeSpan.FromSeconds(20);
                h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                string json = await h.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest").ConfigureAwait(false);
                var r = new Release { Tag = Str(json, "tag_name") };
                r.Version = ParseTag(r.Tag);
                // Each asset's download link ends in its file name; the mod's zip is AAIAccess-<version>.zip.
                var assets = Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"")
                    .Cast<Match>().Select(m => (name: Uri.UnescapeDataString(m.Groups[1].Value.Substring(m.Groups[1].Value.LastIndexOf('/') + 1)), url: m.Groups[1].Value)).ToList();
                var zip = assets.FirstOrDefault(a => Regex.IsMatch(a.name, @"^AAIAccess-[\d.]+\.zip$", RegexOptions.IgnoreCase));
                if (zip.name == null) zip = assets.FirstOrDefault(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && a.name.IndexOf("installer", StringComparison.OrdinalIgnoreCase) < 0);
                r.ZipName = zip.name; r.ZipUrl = zip.url;
                return r;
            }
        }

        private static string Str(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Downloads the release zip to a temporary file. progress: 0-100.</summary>
        public static async Task<string> Download(Release r, Action<int> progress)
        {
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AAIAccess-installer");
            Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, r.ZipName);
            using (var h = Http())
            using (var resp = await h.GetAsync(r.ZipUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1, got = 0;
                int last = -1;
                using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = File.Create(file))
                {
                    var buf = new byte[81920];
                    int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n).ConfigureAwait(false);
                        got += n;
                        if (total > 0) { int p = (int)(got * 100 / total); if (p / 10 != last / 10) { last = p; progress?.Invoke(p); } }
                    }
                }
            }
            Log($"Downloaded {r.ZipName}");
            return file;
        }

        // ---------- installing ----------

        public static bool GameRunning() => Process.GetProcessesByName("GK12").Length > 0;

        /// <summary>Unpacks the release zip into the game folder. Files the player already has in UserData (settings,
        /// renamed rooms, ...) are kept. Returns the number of files written.</summary>
        public static int Extract(string zipFile, string dir)
        {
            string root = Norm(dir) + "\\";
            int written = 0;
            using (var zip = ZipFile.OpenRead(zipFile))
            {
                // The zip may hold the files directly or inside one folder: find where version.dll sits.
                string prefix = "";
                var ver = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("version.dll", StringComparison.OrdinalIgnoreCase)
                                                          && e.FullName.Replace('\\', '/').Count(c => c == '/') <= 1);
                if (ver != null) prefix = ver.FullName.Replace('\\', '/').Substring(0, ver.FullName.Length - "version.dll".Length);
                if (ver == null) throw new InvalidDataException("This zip doesn't look like an AAI Access release (no version.dll).");

                foreach (var e in zip.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    name = name.Substring(prefix.Length);
                    if (name.Length == 0) continue;
                    string target = Norm(System.IO.Path.Combine(dir, name.Replace('/', '\\')));
                    if (!(target + (name.EndsWith("/") ? "\\" : "")).StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // nothing outside the game folder
                    if (name.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    if (name.StartsWith("UserData/", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) continue;   // keep the player's own
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                    written++;
                }
            }
            Log($"Installed {written} files into {dir}");
            return written;
        }

        /// <summary>The .NET 6 runtime MelonLoader runs the mod on. Some PCs have it already.</summary>
        public static bool HasDotNet6()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App"))
                    if (k != null && k.GetValueNames().Any(n => n.StartsWith("6."))) return true;
            }
            catch { }
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string d = System.IO.Path.Combine(pf, "dotnet", "shared", "Microsoft.NETCore.App");
                if (Directory.Exists(d) && Directory.GetDirectories(d, "6.*").Length > 0) return true;
            }
            catch { }
            return false;
        }

        // ---------- uninstalling ----------

        /// <summary>Removes the mod. MelonLoader itself stays when other mods still use it. Returns what was kept.</summary>
        public static string Uninstall(string dir, bool settingsToo)
        {
            void Del(string rel) { string p = System.IO.Path.Combine(dir, rel); if (File.Exists(p)) { File.Delete(p); Log("Deleted " + rel); } }
            void DelDir(string rel) { string p = System.IO.Path.Combine(dir, rel); if (Directory.Exists(p)) { Directory.Delete(p, true); Log("Deleted " + rel); } }

            Del(@"Mods\AAIAccess.dll");
            Del(@"Plugins\AAIAccessLoaderFix.dll");
            Del("prism.dll");
            Del("nvdaControllerClient64.dll");
            Del("GK12CodeDump.dll");
            Del("GK12CodeDump.txt");

            bool others = HasFiles(System.IO.Path.Combine(dir, "Mods"), "*.dll") || HasFiles(System.IO.Path.Combine(dir, "Plugins"), "*.dll");
            string kept = "";
            if (others) kept = "MelonLoader was kept, because other mods in the Mods or Plugins folder still need it.";
            else
            {
                Del("version.dll");
                DelDir("MelonLoader");
                DelDir("Mods");
                DelDir("Plugins");
                DelDir("UserLibs");
            }
            if (settingsToo)
            {
                string ud = System.IO.Path.Combine(dir, "UserData");
                if (Directory.Exists(ud))
                {
                    if (others) foreach (var f in Directory.GetFiles(ud, "AAIAccess*")) { File.Delete(f); Log("Deleted " + f); }
                    else DelDir("UserData");
                }
            }
            return kept;
        }

        private static bool HasFiles(string d, string pattern) => Directory.Exists(d) && Directory.GetFiles(d, pattern).Length > 0;
    }
}
