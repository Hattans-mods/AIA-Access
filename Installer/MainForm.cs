using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AAIAccessInstaller
{
    /// <summary>The installer window. Built for screen readers: every control has a name, the status is a read-only box
    /// that gets the focus when something has been found out (so it is read), and results come as message boxes.</summary>
    internal sealed class MainForm : Form
    {
        private readonly ComboBox _game = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Game folder" };
        private readonly Button _browse = new Button { Text = "&Browse...", AccessibleName = "Browse for the game folder" };
        private readonly TextBox _status = new TextBox { ReadOnly = true, AccessibleName = "Status", TabStop = true };
        private readonly Button _install = new Button { Text = "&Install", AccessibleName = "Install" };
        private readonly Button _check = new Button { Text = "&Check for updates" };
        private readonly Button _uninstall = new Button { Text = "&Uninstall" };
        private readonly Button _close = new Button { Text = "Cl&ose" };

        private Release _latest;
        private string _localZip;          // --zip: install from this file instead of downloading
        private readonly bool _autoInstall; // --install: start installing once the check is done (after restarting as administrator)

        public MainForm(string dir, string zip, bool autoInstall)
        {
            _localZip = zip;
            _autoInstall = autoInstall;
            Text = "AAI Access Installer";
            AccessibleName = Text;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 230);
            CancelButton = _close;

            var lblGame = new Label { Text = "&Game folder:", AutoSize = true, Location = new Point(12, 15) };
            _game.SetBounds(130, 12, 380, 28);
            _browse.SetBounds(520, 11, 88, 30);
            var lblStatus = new Label { Text = "&Status:", AutoSize = true, Location = new Point(12, 60) };
            _status.SetBounds(130, 57, 478, 28);
            _install.SetBounds(130, 110, 220, 36);
            _check.SetBounds(360, 110, 160, 36);
            _uninstall.SetBounds(130, 156, 220, 36);
            _close.SetBounds(360, 156, 160, 36);
            Controls.AddRange(new Control[] { lblGame, _game, _browse, lblStatus, _status, _install, _check, _uninstall, _close });
            int t = 0; foreach (var c in new Control[] { lblGame, _game, _browse, lblStatus, _status, _install, _check, _uninstall, _close }) c.TabIndex = t++;

            _browse.Click += (s, e) => Browse();
            _check.Click += async (s, e) => await Check(true);
            _install.Click += async (s, e) => await Install();
            _uninstall.Click += (s, e) => Uninstall();
            _close.Click += (s, e) => Close();
            _game.SelectedIndexChanged += async (s, e) => { if (_ready) await Check(false); };

            Shown += async (s, e) =>
            {
                FillGames(dir);
                _ready = true;
                await Check(true);
                if (_autoInstall && _install.Enabled) await Install();
            };
        }

        private bool _ready;

        private string Dir => (_game.SelectedItem as GameDir)?.Path;

        private void FillGames(string preferred)
        {
            _game.Items.Clear();
            foreach (var g in Core.FindGames()) _game.Items.Add(g);
            if (!string.IsNullOrEmpty(preferred) && Core.IsGameDir(preferred) && !_game.Items.Cast<GameDir>().Any(g => string.Equals(g.Path, preferred, StringComparison.OrdinalIgnoreCase)))
                _game.Items.Insert(0, new GameDir { Name = "Chosen folder", Path = preferred });
            int i = 0;
            if (!string.IsNullOrEmpty(preferred))
                for (int k = 0; k < _game.Items.Count; k++) if (string.Equals(((GameDir)_game.Items[k]).Path, preferred, StringComparison.OrdinalIgnoreCase)) i = k;
            if (_game.Items.Count > 0) _game.SelectedIndex = i;
        }

        private void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose the folder of Ace Attorney Investigations Collection (the one with GK12.exe in it).", ShowNewFolderButton = false })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!Core.IsGameDir(d.SelectedPath))
                {
                    Message("That folder doesn't have the game in it (GK12.exe is missing). In Steam, open the game's Properties, Installed Files, Browse, to find the right folder.", MessageBoxIcon.Warning);
                    return;
                }
                _ready = false;
                FillGames(d.SelectedPath);
                _ready = true;
                _ = Check(true);
            }
        }

        /// <summary>Says what is installed and what is newest, and sets the buttons to match.</summary>
        private async Task Check(bool speak)
        {
            Busy(true);
            try
            {
                if (Dir == null)
                {
                    _canInstall = _canUninstall = false;
                    SetStatus("The game wasn't found. Press Browse to choose its folder.", speak);
                    return;
                }
                SetStatus("Checking for the newest version...", false);
                string online = "";
                if (_localZip == null)
                {
                    try { _latest = await Core.LatestRelease(); }
                    catch (Exception e) { _latest = null; online = "Couldn't reach GitHub (" + Short(e) + "). "; Core.Log("Check failed: " + e); }
                }
                var have = Core.InstalledVersion(Dir, out bool installed);
                string msg;
                if (_localZip != null)
                {
                    msg = (installed ? "Installed: " + Core.Show(have) + ". " : "AAI Access is not installed. ") + "Ready to install from " + Path.GetFileName(_localZip) + ".";
                    _install.Text = "&Install from file";
                    _canInstall = true;
                }
                else if (_latest == null || _latest.ZipUrl == null)
                {
                    msg = online + (installed ? "Installed: " + Core.Show(have) + "." : "AAI Access is not installed.") + (_latest != null ? " The newest release has no zip to install." : "");
                    _canInstall = false;
                }
                else
                {
                    string newest = Core.Show(_latest.Version);
                    if (!installed) { msg = $"AAI Access is not installed. The newest version is {newest}."; _install.Text = "&Install " + newest; }
                    else if (_latest.Version != null && have < _latest.Version) { msg = $"An update is ready: you have {Core.Show(have)}, the newest is {newest}."; _install.Text = "&Update to " + newest; }
                    else if (_latest.Version != null && have > _latest.Version) { msg = $"You have {Core.Show(have)}, which is newer than the newest release, {newest}."; _install.Text = "&Install the older " + newest; }
                    else { msg = $"You have the newest version, {Core.Show(have)}."; _install.Text = "Re&install " + newest; }
                    _canInstall = true;
                }
                _install.AccessibleName = _install.Text.Replace("&", "");
                _canUninstall = installed || File.Exists(Path.Combine(Dir, "version.dll"));
                Busy(false);
                SetStatus(msg, speak);   // after the buttons are back, so the focus lands on the status box
            }
            finally { Busy(false); }
        }

        private async Task Install()
        {
            string dir = Dir;
            if (dir == null) return;
            if (Core.GameRunning()) { Message("Please close the game first, then press Install again.", MessageBoxIcon.Information); return; }
            Busy(true);
            try
            {
                string zip = _localZip;
                if (zip == null)
                {
                    if (_latest?.ZipUrl == null) { Message("There is nothing to download yet. Press Check for updates.", MessageBoxIcon.Information); return; }
                    var have = Core.InstalledVersion(dir, out bool installed);
                    if (installed && _latest.Version != null && have > _latest.Version &&
                        MessageBox.Show(this, $"Your version, {Core.Show(have)}, is newer than {Core.Show(_latest.Version)}. Replace it with the older version?",
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    SetStatus("Downloading " + _latest.ZipName + "...", false);
                    zip = await Core.Download(_latest, p => BeginInvoke((Action)(() => SetStatus($"Downloading {_latest.ZipName}: {p} percent.", false))));
                }
                SetStatus("Installing...", false);
                await Task.Run(() => Core.Extract(zip, dir));
                var now = Core.InstalledVersion(dir, out _);
                string done = $"AAI Access {Core.Show(now)} is installed. Start the game from Steam. The first start after installing can take a few minutes and may look frozen; leave it running.";
                SetStatus(done, false);
                Message(done, MessageBoxIcon.Information);
                if (!Core.HasDotNet6() &&
                    MessageBox.Show(this, "The mod runs on Microsoft's .NET 6 Desktop Runtime (x64), which wasn't found on this PC. If the game shows an error about .NET when it starts, install it. Open Microsoft's download page now?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    Open(Core.DotNet6Page);
                await Check(true);
            }
            catch (UnauthorizedAccessException) { AskAdmin(dir, "install"); }
            catch (IOException e) when (Core.GameRunning()) { Core.Log(e.ToString()); Message("The game is running, so its files can't be changed. Close the game, then press Install again.", MessageBoxIcon.Warning); }
            catch (Exception e) { Core.Log("Install failed: " + e); Message("Installing didn't work: " + Short(e) + "\n\nYou can also install by hand: download the zip from " + Core.ReleasesPage + " and copy everything in it into the game's folder.", MessageBoxIcon.Error); }
            finally { Busy(false); }
        }

        private void Uninstall()
        {
            string dir = Dir;
            if (dir == null) return;
            if (Core.GameRunning()) { Message("Please close the game first, then press Uninstall again.", MessageBoxIcon.Information); return; }
            if (MessageBox.Show(this, "Remove AAI Access from this game?\n\n" + dir, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            bool settings = MessageBox.Show(this, "Also delete your mod settings and what the mod has learnt (room names, checked spots and the like, in the UserData folder)?\n\nChoose No to keep them for a later install.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            Busy(true);
            try
            {
                string kept = Core.Uninstall(dir, settings);
                Message("AAI Access has been removed. The game now starts without it." + (kept.Length > 0 ? "\n\n" + kept : ""), MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException) { AskAdmin(dir, "uninstall"); }
            catch (Exception e) { Core.Log("Uninstall failed: " + e); Message("Removing didn't work completely: " + Short(e), MessageBoxIcon.Error); }
            finally { Busy(false); }
            _ = Check(true);
        }

        /// <summary>Windows refused a file in the game folder: offer to start again with administrator rights.</summary>
        private void AskAdmin(string dir, string what)
        {
            if (MessageBox.Show(this, "Windows didn't allow changing files in the game folder. Start the installer again as administrator and continue?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                string args = $"--dir \"{dir}\"" + (what == "install" ? " --install" : "") + (_localZip != null ? $" --zip \"{_localZip}\"" : "");
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { Verb = "runas", UseShellExecute = true });
                Close();
            }
            catch (Exception e) { Core.Log("Restart as administrator: " + e.Message); }
        }

        // ---------- small helpers ----------

        private void SetStatus(string text, bool speak)
        {
            _status.Text = text;
            Core.Log("Status: " + text);
            // Moving the focus to the status box makes the screen reader read it.
            if (speak) { _status.Focus(); _status.Select(0, 0); }
        }

        private bool _canInstall, _canUninstall;   // what Check found; the buttons follow it whenever nothing is running

        private void Busy(bool on)
        {
            UseWaitCursor = on;
            _game.Enabled = _browse.Enabled = _check.Enabled = !on;
            _install.Enabled = !on && _canInstall;
            _uninstall.Enabled = !on && _canUninstall;
        }

        private void Message(string text, MessageBoxIcon icon) => MessageBox.Show(this, text, Text, MessageBoxButtons.OK, icon);

        private static string Short(Exception e) => (e.InnerException ?? e).Message;

        private static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
    }
}
