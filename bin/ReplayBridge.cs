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
    // === WinMM MIDI OUT API ===
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MIDIOUTCAPSA {
        public ushort wMid; public ushort wPid; public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public ushort wTechnology; public ushort wVoices; public ushort wNotes;
        public ushort wChannelMask; public uint dwSupport;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MIDIINCAPSA {
        public ushort wMid; public ushort wPid; public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint dwSupport;
    }
    [DllImport("winmm.dll")] public static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Ansi)] public static extern uint midiOutGetDevCapsA(UIntPtr id, out MIDIOUTCAPSA caps, uint cb);
    [DllImport("winmm.dll")] public static extern uint midiOutOpen(out IntPtr h, uint id, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] public static extern uint midiOutShortMsg(IntPtr h, uint msg);
    [DllImport("winmm.dll")] public static extern uint midiOutClose(IntPtr h);
    [DllImport("winmm.dll")] public static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Ansi)] public static extern uint midiInGetDevCapsA(UIntPtr id, out MIDIINCAPSA caps, uint cb);

    // === teVirtualMIDI direct API (fallback when WinMM is broken) ===
    [DllImport("teVirtualMIDI64.dll", EntryPoint = "virtualMIDICreatePortEx3", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern IntPtr virtualMIDICreatePortEx3_64([MarshalAs(UnmanagedType.LPWStr)] string portName, IntPtr callback, IntPtr userData, uint maxSysexLength, uint flags);
    [DllImport("teVirtualMIDI32.dll", EntryPoint = "virtualMIDICreatePortEx3", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern IntPtr virtualMIDICreatePortEx3_32([MarshalAs(UnmanagedType.LPWStr)] string portName, IntPtr callback, IntPtr userData, uint maxSysexLength, uint flags);
    [DllImport("teVirtualMIDI64.dll", EntryPoint = "virtualMIDISendData", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool virtualMIDISendData_64(IntPtr port, byte[] data, uint length);
    [DllImport("teVirtualMIDI32.dll", EntryPoint = "virtualMIDISendData", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool virtualMIDISendData_32(IntPtr port, byte[] data, uint length);
    [DllImport("teVirtualMIDI64.dll", EntryPoint = "virtualMIDIClosePort", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern void virtualMIDIClosePort_64(IntPtr port);
    [DllImport("teVirtualMIDI32.dll", EntryPoint = "virtualMIDIClosePort", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern void virtualMIDIClosePort_32(IntPtr port);

    private static bool is64bit = IntPtr.Size == 8;

    private NotifyIcon trayIcon;
    private ToolStripMenuItem statusItem;
    private ToolStripMenuItem lastKeyItem;
    private ToolStripMenuItem invertWheelItem;
    private ToolStripMenuItem autoStartItem;
    private IntPtr midiHandle = IntPtr.Zero;
    private IntPtr virtualMidiPort = IntPtr.Zero;
    private bool useDirectMidi = false;
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
        hiddenForm = new Form();
        IntPtr forceHandle = hiddenForm.Handle; // Force window handle creation on UI thread

        baseDir = AppDomain.CurrentDomain.BaseDirectory;
        configPath = Path.Combine(baseDir, "config.json");
        InitIcons();
        LoadConfig();

        statusItem = new ToolStripMenuItem("Status: Starting...");
        statusItem.Enabled = false;
        lastKeyItem = new ToolStripMenuItem("Last key: -");
        lastKeyItem.Enabled = false;

        ToolStripMenuItem settingsItem = new ToolStripMenuItem("Wheel Speed Settings...", null, OnOpenSettings);
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);

        invertWheelItem = new ToolStripMenuItem("Invert Wheel Direction (Invert Jog)", null, OnToggleInvert);
        invertWheelItem.Checked = invertWheel;

        autoStartItem = new ToolStripMenuItem("Start automatically with Windows", null, OnToggleAutoStart);
        autoStartItem.Checked = IsAutoStartEnabled();

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(lastKeyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(invertWheelItem);
        menu.Items.Add(autoStartItem);
        menu.Items.Add(new ToolStripMenuItem("Restart Bridge Connection", null, OnRestartBridge));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit Replay Bridge", null, OnExit));

        trayIcon = new NotifyIcon();
        trayIcon.Icon = GetStatusIcon(Color.Orange);
        trayIcon.Text = "Replay Editor Bridge (Double-click for Settings)";
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
        settingsForm.Text = "Replay Editor - Wheel Speed";
        settingsForm.Size = new Size(400, 260);
        settingsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
        settingsForm.MaximizeBox = false;
        settingsForm.MinimizeBox = false;
        settingsForm.StartPosition = FormStartPosition.CenterScreen;

        Label l1 = new Label() { Text = "SLOW JOG Speed (fine):", Left = 20, Top = 25, Width = 220 };
        NumericUpDown n1 = new NumericUpDown() { Left = 250, Top = 22, Width = 100, Minimum = 1, Maximum = 100, Value = slowSpeed };

        Label l2 = new Label() { Text = "JOG Speed (main 1:1):", Left = 20, Top = 65, Width = 220 };
        NumericUpDown n2 = new NumericUpDown() { Left = 250, Top = 62, Width = 100, Minimum = 1, Maximum = 500, Value = jogSpeed };

        Label l3 = new Label() { Text = "SCRL Speed (fast scroll):", Left = 20, Top = 105, Width = 220 };
        NumericUpDown n3 = new NumericUpDown() { Left = 250, Top = 102, Width = 100, Minimum = 10, Maximum = 5000, Increment = 25, Value = scrlSpeed };

        CheckBox cbInv = new CheckBox() { Text = "Invert Wheel Direction (Invert Jog)", Left = 20, Top = 142, Width = 300, Checked = invertWheel };

        Button btnSave = new Button() { Text = "Save and Apply", Left = 120, Top = 175, Width = 150, Height = 32 };
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

    private Form hiddenForm;
    private Bitmap bmpRed, bmpOrange, bmpGreen;
    private Icon iconRed, iconOrange, iconGreen;

    private Icon MakeIcon(Color c, out Bitmap bmp) {
        bmp = new Bitmap(16, 16);
        using (Graphics g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Transparent);
            try {
                using (Bitmap baseImg = new Bitmap(Path.Combine(baseDir, "icon.ico"))) {
                    g.DrawImage(baseImg, 0, 0, 16, 16);
                }
                using (Brush b = new SolidBrush(c)) { g.FillEllipse(b, 10, 10, 6, 6); }
                using (Pen p = new Pen(Color.Black, 1)) { g.DrawEllipse(p, 10, 10, 6, 6); }
            } catch {
                using (Brush b = new SolidBrush(c)) { g.FillEllipse(b, 2, 2, 12, 12); }
                using (Pen p = new Pen(Color.White, 1)) { g.DrawEllipse(p, 2, 2, 12, 12); }
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
        // Zamerne NEVOLAME bmp.Dispose() ani DestroyIcon.
        // Bitmapy si nechame v pameti po celou dobu behu, abychom meli
        // 100% jistotu, ze Windows nevymaze data, na ktere ikona odkazuje.
    }

    private void InitIcons() {
        iconRed = MakeIcon(Color.Red, out bmpRed);
        iconOrange = MakeIcon(Color.Orange, out bmpOrange);
        iconGreen = MakeIcon(Color.LimeGreen, out bmpGreen);
    }

    private Icon GetStatusIcon(Color c) {
        if (c == Color.Red) return iconRed;
        if (c == Color.LimeGreen) return iconGreen;
        return iconOrange;
    }

    private void UpdateStatus(string text, Color color) {
        try {
            if (hiddenForm != null && hiddenForm.InvokeRequired) {
                hiddenForm.BeginInvoke(new Action(delegate { UpdateStatus(text, color); }));
                return;
            }
            string full = "Replay Editor: " + text;
            trayIcon.Text = full.Length > 60 ? full.Substring(0, 60) : full;
            trayIcon.Icon = GetStatusIcon(color);
            statusItem.Text = "Status: " + text;
        } catch (Exception ex) { LogCrash("UpdateStatus", ex); }
    }

    private bool EnsureMidiOpen() {
        if (midiHandle != IntPtr.Zero || virtualMidiPort != IntPtr.Zero) return true;
        uint outCount = midiOutGetNumDevs();
        uint inCount = midiInGetNumDevs();
        try {
            using (StreamWriter sw = new StreamWriter(Path.Combine(baseDir, "midi_debug.txt"), false)) {
                sw.WriteLine(string.Format("Platform: {0}-bit process", is64bit ? "64" : "32"));
                sw.WriteLine(string.Format("MIDI OUT devices: {0}", outCount));
                for (uint i = 0; i < outCount; i++) {
                    MIDIOUTCAPSA caps;
                    uint res = midiOutGetDevCapsA((UIntPtr)i, out caps, (uint)Marshal.SizeOf(typeof(MIDIOUTCAPSA)));
                    sw.WriteLine(string.Format("  OUT[{0}]: name='{1}', result={2}", i, caps.szPname ?? "null", res));
                    if (caps.szPname != null && (
                        caps.szPname.IndexOf("loopMIDI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        caps.szPname.IndexOf("LoopBe", StringComparison.OrdinalIgnoreCase) >= 0)) {
                        uint openRes = midiOutOpen(out midiHandle, i, IntPtr.Zero, IntPtr.Zero, 0);
                        sw.WriteLine(string.Format("  -> WinMM match! midiOutOpen result: {0}", openRes));
                        if (openRes == 0) { useDirectMidi = false; return true; }
                    }
                }
                sw.WriteLine(string.Format("MIDI IN devices: {0}", inCount));
                for (uint i = 0; i < inCount; i++) {
                    MIDIINCAPSA inCaps;
                    uint res = midiInGetDevCapsA((UIntPtr)i, out inCaps, (uint)Marshal.SizeOf(typeof(MIDIINCAPSA)));
                    sw.WriteLine(string.Format("  IN[{0}]: name='{1}', result={2}", i, inCaps.szPname ?? "null", res));
                }
                // WinMM failed - try teVirtualMIDI direct API
                sw.WriteLine("WinMM: loopMIDI port NOT found. Trying teVirtualMIDI direct API...");
                try {
                    IntPtr port = IntPtr.Zero;
                    if (is64bit)
                        port = virtualMIDICreatePortEx3_64("SoftLab ReplayBridge", IntPtr.Zero, IntPtr.Zero, 65535, 1);
                    else
                        port = virtualMIDICreatePortEx3_32("SoftLab ReplayBridge", IntPtr.Zero, IntPtr.Zero, 65535, 1);
                    if (port != IntPtr.Zero) {
                        virtualMidiPort = port;
                        useDirectMidi = true;
                        sw.WriteLine("teVirtualMIDI: Created direct port 'SoftLab ReplayBridge' - SUCCESS");
                        return true;
                    } else {
                        int err = Marshal.GetLastWin32Error();
                        sw.WriteLine(string.Format("teVirtualMIDI: CreatePort failed, error={0}", err));
                    }
                } catch (Exception ex) {
                    sw.WriteLine(string.Format("teVirtualMIDI: DLL not available ({0})", ex.GetType().Name));
                }
            }
        } catch { }
        return false;
    }

    private void SendMidi(uint msg) {
        if (useDirectMidi && virtualMidiPort != IntPtr.Zero) {
            byte[] data = new byte[] { (byte)(msg & 0xFF), (byte)((msg >> 8) & 0xFF), (byte)((msg >> 16) & 0xFF) };
            if (is64bit)
                virtualMIDISendData_64(virtualMidiPort, data, 3);
            else
                virtualMIDISendData_32(virtualMidiPort, data, 3);
        } else if (midiHandle != IntPtr.Zero) {
            midiOutShortMsg(midiHandle, msg);
        }
    }

    private void WorkerLoop() {
        while (running) {
            if (!EnsureMidiOpen()) {
                UpdateStatus("Cekam na loopMIDI/LoopBe...", Color.Red);
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
                            SendMidi(0xB0u | (88u << 8) | (data2 << 16));
                            remaining -= chunk;
                        }
                    } else if (p[0] == "ON") {
                        int note = int.Parse(p[1]);
                        SendMidi(0x90u | ((uint)(note & 0x7F) << 8) | (127u << 16));
                        if (p.Length >= 3) lastKeyItem.Text = "Last key: MIDIKey_" + p[2].Replace("-", "_") + "_DOWN";
                    } else if (p[0] == "OFF") {
                        int note = int.Parse(p[1]);
                        SendMidi(0x80u | ((uint)(note & 0x7F) << 8));
                    } else if (p[0] == "CC" && p.Length >= 3) {
                        int cc = int.Parse(p[1]);
                        int val = int.Parse(p[2]);
                        SendMidi(0xB0u | ((uint)(cc & 0x7F) << 8) | ((uint)(val & 0x7F) << 16));
                    } else if (p[0] == "STATUS") {
                        if (p[1] == "READY") UpdateStatus("Pripojeno (Dvojklik = Nastaveni)", Color.LimeGreen);
                        else if (p[1] == "WAIT_USB") UpdateStatus("Cekam na pripojeni USB pultu...", Color.Orange);
                        else if (p[1] == "BUSY") UpdateStatus("Pult je blokovan (vypni Companion/Resolve)", Color.Red);
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
        if (virtualMidiPort != IntPtr.Zero) {
            try {
                if (is64bit) virtualMIDIClosePort_64(virtualMidiPort);
                else virtualMIDIClosePort_32(virtualMidiPort);
            } catch {}
            virtualMidiPort = IntPtr.Zero;
        }
        trayIcon.Visible = false;
        Application.Exit();
    }

    static string crashLogPath = "C:\\SoftLab_ReplayBridge\\crash.txt";

    static void LogCrash(string source, Exception ex) {
        try {
            File.AppendAllText(crashLogPath, string.Format(
                "\r\n[{0}] {1}: {2}\r\n{3}\r\n",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), source, ex.Message, ex.StackTrace));
        } catch {}
    }

    [STAThread]
    static void Main() {
        Application.ThreadException += (s, e) => LogCrash("ThreadException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null) LogCrash("UnhandledException", ex);
        };
        bool createdNew;
        using (Mutex m = new Mutex(true, "SoftLabReplayBridgeMutex", out createdNew)) {
            if (!createdNew) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try {
                Application.Run(new ReplayTrayApp());
            } catch (Exception ex) {
                LogCrash("Main", ex);
            }
        }
    }
}