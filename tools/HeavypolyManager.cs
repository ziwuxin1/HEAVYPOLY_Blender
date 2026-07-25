// HEAVYPOLY Manager - a small WinForms front-end for install / uninstall / launch.
//
// The actual install logic lives in tools\heavypoly_setup.ps1 so there is a single
// source of truth; this EXE only drives it and shows the output.
//
// Build (no external dependencies - csc.exe ships with Windows):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe ^
//     /out:HEAVYPOLY-Manager.exe /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll ^
//     tools\HeavypolyManager.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Heavypoly
{
    public class MainForm : Form
    {
        const string MANIFEST = ".heavypoly-manifest.json";

        readonly ComboBox _versions = new ComboBox();
        readonly Label _status = new Label();
        readonly Button _install = new Button();
        readonly Button _uninstall = new Button();
        readonly Button _launch = new Button();
        readonly TextBox _log = new TextBox();

        readonly string _exeDir;
        readonly string _script;
        readonly string _configRoot;

        public MainForm()
        {
            _exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            _script = Path.Combine(_exeDir, "tools\\heavypoly_setup.ps1");
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

            var lblVer = new Label
            {
                Text = "Blender version:",
                Location = new Point(20, 62),
                AutoSize = true
            };
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

        string TargetDir
        {
            get
            {
                var v = SelectedVersion;
                return v == null ? null : Path.Combine(_configRoot, v);
            }
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
            found.Sort(delegate(string a, string b)
            {
                return new Version(a).CompareTo(new Version(b));
            });

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
            var dir = TargetDir;
            if (dir == null) return;
            bool installed = File.Exists(Path.Combine(dir, MANIFEST));
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

        // ---------------------------------------------------------------- actions

        void RunAction(string action)
        {
            if (SelectedVersion == null) return;

            if (!File.Exists(_script))
            {
                MessageBox.Show(
                    "Cannot find:\n" + _script +
                    "\n\nKeep HEAVYPOLY-Manager.exe in the repository folder, next to the tools\\ directory.",
                    "HEAVYPOLY", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

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
                try { code = RunPowerShell(action, version); }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }

                var finished = code;
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

        int RunPowerShell(string action, string version)
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + _script + "\"" +
                " -Action " + action + " -BlenderVersion " + version);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = _exeDir;

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
                if (!string.IsNullOrEmpty(pf))
                    roots.Add(Path.Combine(pf, "Blender Foundation"));
            }

            // Prefer the exact version the user selected.
            if (version != null)
            {
                foreach (var root in roots)
                {
                    if (!Directory.Exists(root)) continue;
                    var exact = Path.Combine(root, "Blender " + version, "blender.exe");
                    if (File.Exists(exact)) return exact;
                }
            }

            // Otherwise take the newest Blender we can find.
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
