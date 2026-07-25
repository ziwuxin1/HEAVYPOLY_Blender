// HEAVYPOLY Manager - a self-contained WinForms front-end for install / uninstall / launch.
//
// The whole payload (config\ and scripts\) and the install engine
// (heavypoly_setup.ps1) are embedded as resources, so the compiled exe is a
// single file that works on its own - copy it anywhere and run it.
//
// The install logic itself is NOT reimplemented here: at run time the embedded
// engine is unpacked to a temp folder and driven with -SourceRoot, so there is
// still exactly one implementation shared with install.bat / uninstall.bat.
//
// Build: see tools\build.ps1

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Heavypoly
{
    public class MainForm : Form
    {
        const string MANIFEST = ".heavypoly-manifest.json";
        const string RES_ZIP = "payload.zip";
        const string RES_PS1 = "heavypoly_setup.ps1";

        readonly ComboBox _versions = new ComboBox();
        readonly Label _status = new Label();
        readonly Button _install = new Button();
        readonly Button _uninstall = new Button();
        readonly Button _launch = new Button();
        readonly TextBox _log = new TextBox();

        readonly string _configRoot;

        public MainForm()
        {
            _configRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Blender Foundation\\Blender");
            BuildUi();
            LoadVersions();
        }

        void BuildUi()
        {
            Text = "HEAVYPOLY for Blender";
            ClientSize = new Size(560, 420);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);

            var title = new Label
            {
                Text = "HEAVYPOLY for Blender",
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                Location = new Point(18, 14),
                AutoSize = true
            };
            var lblVer = new Label { Text = "Blender version:", Location = new Point(20, 62), AutoSize = true };

            _versions.Location = new Point(126, 58);
            _versions.Width = 110;
            _versions.DropDownStyle = ComboBoxStyle.DropDownList;
            _versions.SelectedIndexChanged += delegate { RefreshStatus(); };

            _status.Location = new Point(256, 62);
            _status.AutoSize = true;
            _status.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            _install.Text = "Install";
            _install.Location = new Point(20, 98);
            _install.Size = new Size(168, 44);
            _install.Click += delegate { RunAction("install"); };

            _uninstall.Text = "Uninstall";
            _uninstall.Location = new Point(196, 98);
            _uninstall.Size = new Size(168, 44);
            _uninstall.Click += delegate { RunAction("uninstall"); };

            _launch.Text = "Launch Blender";
            _launch.Location = new Point(372, 98);
            _launch.Size = new Size(168, 44);
            _launch.Click += delegate { LaunchBlender(); };

            _log.Location = new Point(20, 158);
            _log.Size = new Size(520, 240);
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Color.FromArgb(30, 30, 30);
            _log.ForeColor = Color.Gainsboro;
            _log.Font = new Font("Consolas", 9F);

            Controls.AddRange(new Control[]
            { title, lblVer, _versions, _status, _install, _uninstall, _launch, _log });
        }

        // ---------------------------------------------------------------- helpers

        void Log(string line)
        {
            if (_log.InvokeRequired) { _log.BeginInvoke((Action<string>)Log, line); return; }
            _log.AppendText(line + Environment.NewLine);
        }

        string SelectedVersion
        {
            get { return _versions.SelectedItem == null ? null : _versions.SelectedItem.ToString(); }
        }

        void LoadVersions()
        {
            _versions.Items.Clear();
            var found = new List<string>();
            if (Directory.Exists(_configRoot))
            {
                foreach (var d in Directory.GetDirectories(_configRoot))
                {
                    var name = Path.GetFileName(d);
                    if (Regex.IsMatch(name, @"^\d+\.\d+$")) found.Add(name);
                }
            }
            found.Sort(delegate(string a, string b) { return new Version(a).CompareTo(new Version(b)); });
            foreach (var v in found) _versions.Items.Add(v);

            if (found.Count == 0)
            {
                _status.Text = "no Blender config found";
                _status.ForeColor = Color.Firebrick;
                _install.Enabled = false;
                _uninstall.Enabled = false;
                Log("No Blender user-config folder found under:");
                Log("  " + _configRoot);
                Log("Start Blender once so it creates one, then reopen this tool.");
                return;
            }
            _versions.SelectedIndex = found.Count - 1;   // newest
            RefreshStatus();
        }

        void RefreshStatus()
        {
            var v = SelectedVersion;
            if (v == null) return;
            bool installed = File.Exists(Path.Combine(Path.Combine(_configRoot, v), MANIFEST));
            _status.Text = installed ? "INSTALLED" : "not installed";
            _status.ForeColor = installed ? Color.SeaGreen : Color.Gray;
            _install.Enabled = true;
            _uninstall.Enabled = installed;
        }

        void SetBusy(bool busy)
        {
            _install.Enabled = !busy;
            _uninstall.Enabled = !busy;
            _launch.Enabled = !busy;
            _versions.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (!busy) RefreshStatus();
        }

        // ------------------------------------------------- embedded payload

        // Unpack the embedded engine + payload into a fresh temp folder.
        // Returns the temp root; caller must delete it.
        static string Unpack()
        {
            var asm = Assembly.GetExecutingAssembly();
            string tmp = Path.Combine(Path.GetTempPath(), "heavypoly_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);

            using (var s = asm.GetManifestResourceStream(RES_PS1))
            {
                if (s == null) throw new Exception("Embedded resource missing: " + RES_PS1);
                using (var f = File.Create(Path.Combine(tmp, RES_PS1))) s.CopyTo(f);
            }

            string src = Path.Combine(tmp, "src");
            Directory.CreateDirectory(src);
            using (var s = asm.GetManifestResourceStream(RES_ZIP))
            {
                if (s == null) throw new Exception("Embedded resource missing: " + RES_ZIP);
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;   // directory entry
                        string dest = Path.Combine(src, entry.FullName.Replace('/', '\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        entry.ExtractToFile(dest, true);
                    }
                }
            }
            return tmp;
        }

        // ---------------------------------------------------------------- actions

        void RunAction(string action)
        {
            if (SelectedVersion == null) return;

            if (action == "uninstall")
            {
                var r = MessageBox.Show(
                    "Remove HEAVYPOLY from Blender " + SelectedVersion + "?\n\n" +
                    "Only the files recorded at install time are deleted, and anything that was " +
                    "replaced is restored. Your own scripts are left alone.",
                    "Confirm uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }

            _log.Clear();
            SetBusy(true);
            var version = SelectedVersion;

            var t = new Thread(delegate()
            {
                int code = -1;
                string tmp = null;
                try
                {
                    tmp = Unpack();
                    code = RunPowerShell(tmp, action, version);
                }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
                finally
                {
                    if (tmp != null)
                    {
                        try { Directory.Delete(tmp, true); } catch { }
                    }
                }

                int finished = code;
                BeginInvoke((Action)delegate
                {
                    SetBusy(false);
                    if (finished == 0 && action == "install")
                    {
                        Log("");
                        Log("Restart Blender, then press Ctrl+Space in the 3D viewport.");
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        int RunPowerShell(string tmp, string action, string version)
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(tmp, RES_PS1) + "\"" +
                " -Action " + action +
                " -BlenderVersion " + version +
                " -SourceRoot \"" + Path.Combine(tmp, "src") + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (var p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                { if (e.Data != null) Log(e.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                { if (e.Data != null) Log(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        void LaunchBlender()
        {
            var exe = FindBlenderExe(SelectedVersion);
            if (exe == null)
            {
                MessageBox.Show(
                    "Could not find blender.exe for version " + (SelectedVersion ?? "?") + ".\n\n" +
                    "Looked under Program Files\\Blender Foundation\\.\n" +
                    "Launch Blender yourself, or install it in the default location.",
                    "HEAVYPOLY", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                var psi = new ProcessStartInfo(exe);
                psi.UseShellExecute = true;
                Process.Start(psi);
                Log("Launched: " + exe);
            }
            catch (Exception ex) { Log("ERROR launching Blender: " + ex.Message); }
        }

        string FindBlenderExe(string version)
        {
            var roots = new List<string>();
            foreach (var env in new string[] { "ProgramFiles", "ProgramFiles(x86)" })
            {
                var pf = Environment.GetEnvironmentVariable(env);
                if (!string.IsNullOrEmpty(pf)) roots.Add(Path.Combine(pf, "Blender Foundation"));
            }

            if (version != null)
            {
                foreach (var root in roots)
                {
                    if (!Directory.Exists(root)) continue;
                    var exact = Path.Combine(root, "Blender " + version, "blender.exe");
                    if (File.Exists(exact)) return exact;
                }
            }

            string best = null;
            Version bestVer = null;
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var d in Directory.GetDirectories(root))
                {
                    var exe = Path.Combine(d, "blender.exe");
                    if (!File.Exists(exe)) continue;
                    var m = Regex.Match(Path.GetFileName(d), @"(\d+\.\d+)");
                    var v = m.Success ? new Version(m.Groups[1].Value) : new Version(0, 0);
                    if (bestVer == null || v > bestVer) { bestVer = v; best = exe; }
                }
            }
            return best;
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
