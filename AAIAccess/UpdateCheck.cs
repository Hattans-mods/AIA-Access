using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AAIAccess
{
    /// <summary>Once per start, asks GitHub for the newest release of the mod and, if it is newer than this one, says so
    /// on the title screen. It only reads the public release list and sends nothing about the player. "noupdate" in
    /// UserData/AAIAccess_off.txt switches it off.</summary>
    internal static class UpdateCheck
    {
        private const string Api = "https://api.github.com/repos/Hattans-mods/AIA-Access/releases/latest";
        private static volatile string _newer;   // set by the background check
        private static bool _said;

        public static void Start()
        {
            try
            {
                string f = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AAIAccess_off.txt");
                if (System.IO.File.Exists(f) && System.IO.File.ReadAllText(f).ToLowerInvariant().Contains("noupdate"))
                { Main.Log.Msg("Update check off (AAIAccess_off.txt says noupdate)."); return; }
            }
            catch { }
            Task.Run(async () =>
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("AAIAccess-UpdateCheck");   // GitHub requires one
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                    string json = await http.GetStringAsync(Api).ConfigureAwait(false);
                    var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"[^\"0-9]*([0-9]+(?:\\.[0-9]+){1,3})");
                    if (!m.Success) { Main.Log.Msg("Update check: no version in GitHub's answer."); return; }
                    var latest = Trim(new Version(m.Groups[1].Value));
                    var mine = Trim(new Version(Main.Version));
                    Main.Log.Msg($"Update check: this is {mine}, the newest on GitHub is {latest}.");
                    if (latest > mine) _newer = latest.ToString(3);
                }
                catch (Exception e) { Main.Log.Msg("Update check: GitHub not reached (" + (e.InnerException ?? e).Message + ")."); }
            });
        }

        private static Version Trim(Version v) => new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

        /// <summary>Per frame: says it once, when the title screen has had time to be read (from 20 s after start).</summary>
        public static void Poll()
        {
            if (_said || _newer == null) return;
            if (UnityEngine.Time.unscaledTime < 20f) return;
            _said = true;
            Speech.Announce(Pad.F("AAI Access {0} is available. Run the installer to update.", _newer));
        }
    }
}
