using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AAIAccessInstaller
{
    internal static class Program
    {
        /// <summary>The log of what the installer did: %TEMP%\AAIAccess-installer\installer.log.</summary>
        public static readonly string LogFile = Path.Combine(Path.GetTempPath(), "AAIAccess-installer", "installer.log");

        /// <summary>
        /// AAIAccess-Installer.exe                       the window
        ///   --dir "game folder"                         choose this copy of the game
        ///   --zip "AAIAccess-x.y.z.zip"                 install from this file instead of downloading
        ///   --install                                   start installing right away (used when restarting as administrator)
        ///   --test check | install DIR ZIP | uninstall DIR [settings]   no window; results go to the log (for development)
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(LogFile)); } catch { }
            Core.Log = s => { try { File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + s + Environment.NewLine); } catch { } };
            Core.Log("AAI Access Installer " + typeof(Program).Assembly.GetName().Version + " started: " + string.Join(" ", args));

            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

            int t = Array.IndexOf(args, "--test");
            if (t >= 0) return Test(args.Skip(t + 1).ToArray());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(Arg("--dir"), Arg("--zip"), args.Contains("--install")));
            return 0;
        }

        private static int Test(string[] a)
        {
            try
            {
                switch (a.FirstOrDefault())
                {
                    case "check":
                        foreach (var g in Core.FindGames()) Core.Log($"Found: {g} installed={Core.Show(Core.InstalledVersion(g.Path, out bool i))} ({i})");
                        var r = Core.LatestRelease().GetAwaiter().GetResult();
                        Core.Log($"Latest: tag={r.Tag} version={Core.Show(r.Version)} zip={r.ZipName} url={r.ZipUrl}");
                        Core.Log(".NET 6 present: " + Core.HasDotNet6());
                        return 0;
                    case "install":
                        Core.Extract(a[2], a[1]);
                        Core.Log("Installed version now: " + Core.Show(Core.InstalledVersion(a[1], out _)));
                        return 0;
                    case "uninstall":
                        string kept = Core.Uninstall(a[1], a.Length > 2 && a[2] == "settings");
                        Core.Log("Uninstalled. " + kept);
                        return 0;
                }
                Core.Log("Unknown test: " + string.Join(" ", a));
                return 2;
            }
            catch (Exception e) { Core.Log("Test failed: " + e); return 1; }
        }
    }
}
