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
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
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

        // Blender-ish dark palette
        static readonly Color Bg        = Color.FromArgb(43, 43, 43);
        static readonly Color Panel     = Color.FromArgb(24, 24, 24);
        static readonly Color Fg        = Color.FromArgb(222, 222, 222);
        static readonly Color TextMuted = Color.FromArgb(140, 140, 140);
        static readonly Color Accent    = Color.FromArgb(237, 126, 22);   // Blender orange
        static readonly Color AccentHot = Color.FromArgb(255, 149, 51);
        static readonly Color Neutral   = Color.FromArgb(62, 62, 62);
        static readonly Color NeutralHot= Color.FromArgb(82, 82, 82);
        static readonly Color OkGreen   = Color.FromArgb(122, 192, 106);

        readonly ComboBox _versions = new ComboBox();
        readonly Label _status = new Label();
        Button _install;
        Button _uninstall;
        Button _launch;
        readonly TextBox _log = new TextBox();

        readonly string _configRoot;

        // Win11 dark title bar
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 on older Win10 builds)
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            }
            catch { /* pre-Win10, ignore */ }
        }

        public MainForm()
        {
            _configRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Blender Foundation\\Blender");
            BuildUi();
            LoadVersions();
        }

        // ---------------------------------------------------------------- ui

        static Icon MakeIcon()
        {
            // Drawn at run time so no .ico binary has to be shipped.
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Accent))
                    g.FillEllipse(b, 1, 1, 30, 30);
                using (var f = new Font("Segoe UI", 15F, FontStyle.Bold))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat
                    { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("H", f, b, new RectangleF(0, 0, 32, 33), sf);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        static Button MakeButton(string text, Point at, Size size, Color back, Color hot)
        {
            var b = new Button
            {
                Text = text,
                Location = at,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9.75F),
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = hot;
            b.FlatAppearance.MouseDownBackColor = back;
            return b;
        }

        void BuildUi()
        {
            Text = "HEAVYPOLY for Blender";
            ClientSize = new Size(600, 440);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9F);
            try { Icon = MakeIcon(); } catch { }

            var title = new Label
            {
                Text = "HEAVYPOLY",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = Fg,
                Location = new Point(26, 20),
                AutoSize = true
            };
            var sub = new Label
            {
                Text = "pie-menu workflow for Blender",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextMuted,
                Location = new Point(29, 60),
                AutoSize = true
            };
            var rule = new Panel
            {
                Location = new Point(26, 88),
                Size = new Size(548, 1),
                BackColor = Color.FromArgb(64, 64, 64)
            };

            var lblVer = new Label
            {
                Text = "Blender",
                ForeColor = TextMuted,
                Location = new Point(26, 108),
                AutoSize = true
            };
            _versions.Location = new Point(86, 104);
            _versions.Width = 96;
            _versions.DropDownStyle = ComboBoxStyle.DropDownList;
            _versions.FlatStyle = FlatStyle.Flat;
            _versions.BackColor = Neutral;
            _versions.ForeColor = Fg;
            _versions.Font = new Font("Segoe UI", 9F);
            _versions.SelectedIndexChanged += delegate { RefreshStatus(); };

            _status.Location = new Point(200, 108);
            _status.AutoSize = true;
            _status.Font = new Font("Segoe UI Semibold", 9F);

            int by = 146, bw = 176, bh = 46;
            _install   = MakeButton("Install",        new Point(26, by),            new Size(bw, bh), Accent,  AccentHot);
            _uninstall = MakeButton("Uninstall",      new Point(26 + bw + 10, by),  new Size(bw, bh), Neutral, NeutralHot);
            _launch    = MakeButton("Launch Blender", new Point(26 + 2*(bw+10), by),new Size(bw, bh), Neutral, NeutralHot);
            _install.Click   += delegate { RunAction("install"); };
            _uninstall.Click += delegate { RunAction("uninstall"); };
            _launch.Click    += delegate { LaunchBlender(); };

            _log.Location = new Point(26, 212);
            _log.Size = new Size(548, 196);
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Panel;
            _log.ForeColor = Color.FromArgb(190, 190, 190);
            _log.Font = new Font("Consolas", 9F);
            _log.TabStop = false;

            // give the log a border without a 3D frame
            var logFrame = new Panel
            {
                Location = new Point(24, 210),
                Size = new Size(552, 200),
                BackColor = Color.FromArgb(64, 64, 64)
            };
            var logInner = new Panel
            {
                Location = new Point(1, 1),
                Size = new Size(550, 198),
                BackColor = Panel
            };
            logFrame.Controls.Add(logInner);
            _log.Location = new Point(8, 6);
            _log.Size = new Size(536, 188);
            logInner.Controls.Add(_log);

            Controls.AddRange(new Control[]
            { title, sub, rule, lblVer, _versions, _status, _install, _uninstall, _launch, logFrame });
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
                _status.Text = "no Blender found";
                _status.ForeColor = Color.FromArgb(215, 100, 90);
                _install.Enabled = false;
                _uninstall.Enabled = false;
                Log("No Blender user-config folder found under:");
                Log("  " + _configRoot);
                Log("");
                Log("Start Blender once so it creates one, then reopen this tool.");
                return;
            }
            _versions.SelectedIndex = found.Count - 1;   // newest
            RefreshStatus();
            Log("Ready.  Blender " + SelectedVersion + " selected.");
            Log("Install copies the HEAVYPOLY config and records what it wrote,");
            Log("so Uninstall can remove exactly those files and nothing else.");
        }

        void RefreshStatus()
        {
            var v = SelectedVersion;
            if (v == null) return;
            bool installed = File.Exists(Path.Combine(Path.Combine(_configRoot, v), MANIFEST));
            _status.Text = installed ? "●  INSTALLED" : "○  not installed";
            _status.ForeColor = installed ? OkGreen : TextMuted;
            _install.Enabled = true;
            _uninstall.Enabled = installed;
            _uninstall.ForeColor = installed ? Color.White : TextMuted;
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
                    if (tmp != null) { try { Directory.Delete(tmp, true); } catch { } }
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
