using System;
using System.IO;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

class ReplayTrayApp : ApplicationContext {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MIDIOUTCAPSA {
        public ushort wMid; public ushort wPid; public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public ushort wTechnology; public ushort wVoices; public ushort wNotes;
        public ushort wChannelMask; public uint dwSupport;
    }
    [DllImport("winmm.dll", CharSet = CharSet.Ansi)] public static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Ansi)] public static extern uint midiOutGetDevCapsA(UIntPtr id, out MIDIOUTCAPSA caps, uint cb);
    [DllImport("winmm.dll")] public static extern uint midiOutOpen(out IntPtr h, uint id, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] public static extern uint midiOutShortMsg(IntPtr h, uint msg);
    [DllImport("winmm.dll")] public static extern uint midiOutClose(IntPtr h);

    private NotifyIcon trayIcon;
    private ToolStripMenuItem statusItem;
    private ToolStripMenuItem lastKeyItem;
    private ToolStripMenuItem invertWheelItem;
    private ToolStripMenuItem autoStartItem;
    private IntPtr midiHandle = IntPtr.Zero;
    private Process nodeProc = null;
    private volatile bool running = true;
    private volatile bool invertWheel = false;
    private int slowSpeed = 3;
    private int jogSpeed = 10;
    private int scrlSpeed = 350;
    private Thread workerThread;
    private string baseDir;
    private string configPath;
    private Form settingsForm = null;

    public ReplayTrayApp() {
        baseDir = AppDomain.CurrentDomain.BaseDirectory;
        configPath = Path.Combine(baseDir, "config.json");
        LoadConfig();

        statusItem = new ToolStripMenuItem("Stav: Startuji...");
        statusItem.Enabled = false;
        lastKeyItem = new ToolStripMenuItem("Posledni tlacitko: -");
        lastKeyItem.Enabled = false;

        ToolStripMenuItem settingsItem = new ToolStripMenuItem("Nastaveni rychlosti kolecka...", null, OnOpenSettings);
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);

        invertWheelItem = new ToolStripMenuItem("Otocit smer kolecka (Invert Jog)", null, OnToggleInvert);
        invertWheelItem.Checked = invertWheel;

        autoStartItem = new ToolStripMenuItem("Spoustet automaticky po startu Windows", null, OnToggleAutoStart);
        autoStartItem.Checked = IsAutoStartEnabled();

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(lastKeyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(invertWheelItem);
        menu.Items.Add(autoStartItem);
        menu.Items.Add(new ToolStripMenuItem("Restartovat spojeni", null, OnRestartBridge));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Ukoncit Replay Bridge", null, OnExit));

        trayIcon = new NotifyIcon();
        trayIcon.Icon = CreateCircleIcon(Color.Orange);
        trayIcon.Text = "Replay Editor Bridge (Dvojklik = Nastaveni rychlosti)";
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += OnOpenSettings;
        trayIcon.Visible = true;

        workerThread = new Thread(WorkerLoop);
        workerThread.IsBackground = true;
        workerThread.Start();
    }

    private void LoadConfig() {
        try {
            if (File.Exists(configPath)) {
                string txt = File.ReadAllText(configPath);
                Match m1 = Regex.Match(txt, "\"slowSpeed\"\\s*:\\s*(\\d+)");
                Match m2 = Regex.Match(txt, "\"jogSpeed\"\\s*:\\s*(\\d+)");
                Match m3 = Regex.Match(txt, "\"scrlSpeed\"\\s*:\\s*(\\d+)");
                Match m4 = Regex.Match(txt, "\"invertWheel\"\\s*:\\s*(true|false)");
                if (m1.Success) slowSpeed = int.Parse(m1.Groups[1].Value);
                if (m2.Success) jogSpeed = int.Parse(m2.Groups[1].Value);
                if (m3.Success) scrlSpeed = int.Parse(m3.Groups[1].Value);
                if (m4.Success) invertWheel = m4.Groups[1].Value == "true";
            }
        } catch {}
    }

    private void SaveConfig() {
        try {
            string json = "{\r\n" +
                "  \"slowSpeed\": " + slowSpeed + ",\r\n" +
                "  \"jogSpeed\": " + jogSpeed + ",\r\n" +
                "  \"scrlSpeed\": " + scrlSpeed + ",\r\n" +
                "  \"invertWheel\": " + (invertWheel ? "true" : "false") + "\r\n}";
            File.WriteAllText(configPath, json);
        } catch {}
    }

    private void OnOpenSettings(object sender, EventArgs e) {
        if (settingsForm != null && !settingsForm.IsDisposed) {
            settingsForm.Activate();
            return;
        }
        settingsForm = new Form();
        settingsForm.Text = "Replay Editor - Rychlost kolecka";
        settingsForm.Size = new Size(380, 260);
        settingsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
        settingsForm.MaximizeBox = false;
        settingsForm.MinimizeBox = false;
        settingsForm.StartPosition = FormStartPosition.CenterScreen;

        Label l1 = new Label() { Text = "SLOW JOG rychlost (jemny):", Left = 20, Top = 25, Width = 200 };
        NumericUpDown n1 = new NumericUpDown() { Left = 230, Top = 22, Width = 100, Minimum = 1, Maximum = 100, Value = slowSpeed };

        Label l2 = new Label() { Text = "JOG rychlost (hlavni 1:1):", Left = 20, Top = 65, Width = 200 };
        NumericUpDown n2 = new NumericUpDown() { Left = 230, Top = 62, Width = 100, Minimum = 1, Maximum = 500, Value = jogSpeed };

        Label l3 = new Label() { Text = "SCRL rychlost (mega rychlost):", Left = 20, Top = 105, Width = 200 };
        NumericUpDown n3 = new NumericUpDown() { Left = 230, Top = 102, Width = 100, Minimum = 10, Maximum = 5000, Increment = 25, Value = scrlSpeed };

        CheckBox cbInv = new CheckBox() { Text = "Otocit smer kolecka (Invert Jog)", Left = 20, Top = 142, Width = 300, Checked = invertWheel };

        Button btnSave = new Button() { Text = "Ulozit a pouzit", Left = 110, Top = 175, Width = 150, Height = 32 };
        btnSave.Click += (s, ev) => {
            slowSpeed = (int)n1.Value;
            jogSpeed = (int)n2.Value;
            scrlSpeed = (int)n3.Value;
            invertWheel = cbInv.Checked;
            invertWheelItem.Checked = invertWheel;
            SaveConfig();
            settingsForm.Close();
        };

        settingsForm.Controls.AddRange(new Control[] { l1, n1, l2, n2, l3, n3, cbInv, btnSave });
        settingsForm.Show();
    }

    private Icon CreateCircleIcon(Color c) {
        Bitmap bmp = new Bitmap(16, 16);
        using (Graphics g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Transparent);
            using (Brush b = new SolidBrush(c)) { g.FillEllipse(b, 2, 2, 12, 12); }
            using (Pen p = new Pen(Color.White, 1)) { g.DrawEllipse(p, 2, 2, 12, 12); }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void UpdateStatus(string text, Color color) {
        try {
            string full = "Replay Editor: " + text;
            trayIcon.Text = full.Substring(0, Math.Min(60, full.Length));
            trayIcon.Icon = CreateCircleIcon(color);
            statusItem.Text = "Stav: " + text;
        } catch {}
    }

    private bool EnsureMidiOpen() {
        if (midiHandle != IntPtr.Zero) return true;
        uint count = midiOutGetNumDevs();
        for (uint i = 0; i < count; i++) {
            MIDIOUTCAPSA caps;
            midiOutGetDevCapsA((UIntPtr)i, out caps, (uint)Marshal.SizeOf(typeof(MIDIOUTCAPSA)));
            if (caps.szPname != null && caps.szPname.IndexOf("loopMIDI", StringComparison.OrdinalIgnoreCase) >= 0) {
                if (midiOutOpen(out midiHandle, i, IntPtr.Zero, IntPtr.Zero, 0) == 0) return true;
            }
        }
        return false;
    }

    private void WorkerLoop() {
        while (running) {
            if (!EnsureMidiOpen()) {
                UpdateStatus("Cekam na loopMIDI Port...", Color.Red);
                Thread.Sleep(3000);
                continue;
            }
            try {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Path.Combine(baseDir, "node.exe");
                psi.Arguments = "\"" + Path.Combine(baseDir, "bridge.mjs") + "\"";
                psi.WorkingDirectory = baseDir;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;

                nodeProc = Process.Start(psi);
                string line;
                while (running && (line = nodeProc.StandardOutput.ReadLine()) != null) {
                    string[] p = line.Split('|');
                    if (p.Length < 2) continue;
                    if (p[0] == "WHEEL") {
                        int steps = int.Parse(p[1]);
                        if (invertWheel) steps = -steps;
                        int remaining = Math.Abs(steps);
                        bool dirUp = steps > 0;
                        int maxPacks = 12;
                        while (remaining > 0 && maxPacks-- > 0) {
                            int chunk = Math.Min(60, remaining);
                            uint data2 = dirUp ? (uint)(64 + chunk) : (uint)chunk;
                            midiOutShortMsg(midiHandle, 0xB0u | (88u << 8) | (data2 << 16));
                            remaining -= chunk;
                        }
                    } else if (p[0] == "ON") {
                        int note = int.Parse(p[1]);
                        midiOutShortMsg(midiHandle, 0x90u | ((uint)(note & 0x7F) << 8) | (127u << 16));
                        if (p.Length >= 3) lastKeyItem.Text = "Posledni: MIDIKey_" + p[2].Replace("-", "_") + "_DOWN";
                    } else if (p[0] == "OFF") {
                        int note = int.Parse(p[1]);
                        midiOutShortMsg(midiHandle, 0x80u | ((uint)(note & 0x7F) << 8));
                    } else if (p[0] == "CC" && p.Length >= 3) {
                        int cc = int.Parse(p[1]);
                        int val = int.Parse(p[2]);
                        midiOutShortMsg(midiHandle, 0xB0u | ((uint)(cc & 0x7F) << 8) | ((uint)(val & 0x7F) << 16));
                    } else if (p[0] == "STATUS") {
                        if (p[1] == "READY") UpdateStatus("Pripojeno (Dvojklik = Nastaveni)", Color.LimeGreen);
                        else if (p[1] == "WAIT_USB") UpdateStatus("Cekam na pripojeni USB pultu...", Color.Orange);
                        else if (p[1] == "BUSY") UpdateStatus("Pult je blokovan (vypni Companion)", Color.Red);
                        else UpdateStatus("Odpojeno, zkusim znovu...", Color.Orange);
                    }
                }
            } catch {}
            if (running) Thread.Sleep(3000);
        }
    }

    private void OnToggleInvert(object sender, EventArgs e) {
        invertWheel = !invertWheel;
        invertWheelItem.Checked = invertWheel;
        SaveConfig();
    }

    private bool IsAutoStartEnabled() {
        try {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false)) {
                return k != null && k.GetValue("SoftLabReplayBridge") != null;
            }
        } catch { return false; }
    }

    private void OnToggleAutoStart(object sender, EventArgs e) {
        try {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true)) {
                if (autoStartItem.Checked) {
                    k.DeleteValue("SoftLabReplayBridge", false);
                    autoStartItem.Checked = false;
                } else {
                    k.SetValue("SoftLabReplayBridge", "\"" + Application.ExecutablePath + "\"");
                    autoStartItem.Checked = true;
                }
            }
        } catch {}
    }

    private void OnRestartBridge(object sender, EventArgs e) {
        try { if (nodeProc != null && !nodeProc.HasExited) nodeProc.Kill(); } catch {}
    }

    private void OnExit(object sender, EventArgs e) {
        running = false;
        try { if (nodeProc != null && !nodeProc.HasExited) nodeProc.Kill(); } catch {}
        if (midiHandle != IntPtr.Zero) { midiOutClose(midiHandle); midiHandle = IntPtr.Zero; }
        trayIcon.Visible = false;
        Application.Exit();
    }

    [STAThread]
    static void Main() {
        bool createdNew;
        using (Mutex m = new Mutex(true, "SoftLabReplayBridgeMutex", out createdNew)) {
            if (!createdNew) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ReplayTrayApp());
        }
    }
}