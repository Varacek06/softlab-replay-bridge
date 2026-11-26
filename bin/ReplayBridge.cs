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
    [DllImport("teVirtualMIDI64.dll", EntryPoint = "virtualMIDICreatePortEx2", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr virtualMIDICreatePortEx2_64([MarshalAs(UnmanagedType.LPWStr)] string portName, IntPtr callback, IntPtr userData, uint maxSysexLength, uint flags);
    [DllImport("teVirtualMIDI32.dll", EntryPoint = "virtualMIDICreatePortEx2", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr virtualMIDICreatePortEx2_32([MarshalAs(UnmanagedType.LPWStr)] string portName, IntPtr callback, IntPtr userData, uint maxSysexLength, uint flags);
    [DllImport("teVirtualMIDI64.dll", EntryPoint = "virtualMIDISendData", SetLastError = true)]
    public static extern bool virtualMIDISendData_64(IntPtr port, byte[] data, uint length);
    [DllImport("teVirtualMIDI32.dll", EntryPoint = "virtualMIDISendData", SetLastError = true)]
    public static extern bool virtualMIDISendData_32(IntPtr port, byte[] data, uint length);
    [DllImport("teVirtualMIDI64.dll", EntryPoint = "virtualMIDIClosePort", SetLastError = true)]
    public static extern void virtualMIDIClosePort_64(IntPtr port);
    [DllImport("teVirtualMIDI32.dll", EntryPoint = "virtualMIDIClosePort", SetLastError = true)]
    public static extern void virtualMIDIClosePort_32(IntPtr port);

    private static bool is64bit = IntPtr.Size == 8;

    private NotifyIcon trayIcon;
    private ToolStripMenuItem statusItem;
    private ToolStripMenuItem lastKeyItem;
    private ToolStripMenuItem invertWheelItem;
    private ToolStripMenuItem autoStartItem;
    private IntPtr midiHandle = IntPtr.Zero;
    private IntPtr virtualMidiPort = IntPtr.Zero;
    private readonly object midiLock = new object();
    private bool useDirectMidi = false;
    private Process nodeProc = null;
    private volatile bool running = true;
    private volatile bool invertWheel = false;
    private float slowSpeed = 3f;
    private float jogSpeed = 10f;
    private float scrlSpeed = 350f;
    private bool jogCurveEnabled = true;
    private float jogCurveThreshold = 30f;
    private float jogCurveRate = 0.015f;
    private float jogCurveMaxMultiplier = 20f;
    private int shuttleDoubleClickMs = 350;
    private float shuttleSensitivity = 80f;
    private float shuttleDeadzone = 0.03f;
    private volatile bool shuttleMidi14Bit = false;
    private volatile bool shuttleActive = false;
    private Thread workerThread;
    private string baseDir;
    private string configPath;
    private Form settingsForm = null;
    public ReplayTrayApp() {
        hiddenForm = new Form();
        IntPtr forceHandle = hiddenForm.Handle; // Force window handle creation on UI thread

        baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoftLabReplayBridge");
        if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);
        configPath = Path.Combine(appDataPath, "config.json");
        
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

    private float ReadFloat(string txt, string key, float fallback, float minVal) {
        Match m = Regex.Match(txt, "\"" + key + "\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)");
        if (m.Success) {
            float val;
            if (float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val)) {
                if (val >= minVal) return val;
            }
        }
        return fallback;
    }

    private int ReadInt(string txt, string key, int fallback, int minVal) {
        Match m = Regex.Match(txt, "\"" + key + "\"\\s*:\\s*(-?[0-9]+)");
        if (m.Success) {
            int val;
            if (int.TryParse(m.Groups[1].Value, out val) && val >= minVal) return val;
        }
        return fallback;
    }

    private bool ReadBool(string txt, string key, bool fallback) {
        Match m = Regex.Match(txt, "\"" + key + "\"\\s*:\\s*(true|false)");
        if (m.Success) return m.Groups[1].Value == "true";
        return fallback;
    }

    private void LoadConfigFromFile(string path) {
        try {
            if (File.Exists(path)) {
                string txt = File.ReadAllText(path);
                slowSpeed = ReadFloat(txt, "slowSpeed", slowSpeed, 0.1f);
                jogSpeed = ReadFloat(txt, "jogSpeed", jogSpeed, 0.1f);
                scrlSpeed = ReadFloat(txt, "scrlSpeed", scrlSpeed, 0.1f);
                invertWheel = ReadBool(txt, "invertWheel", invertWheel);
                jogCurveEnabled = ReadBool(txt, "jogCurveEnabled", jogCurveEnabled);
                jogCurveThreshold = ReadFloat(txt, "jogCurveThreshold", jogCurveThreshold, 0f);
                jogCurveRate = ReadFloat(txt, "jogCurveRate", jogCurveRate, 0f);
                jogCurveMaxMultiplier = ReadFloat(txt, "jogCurveMaxMultiplier", jogCurveMaxMultiplier, 1f);
                shuttleDoubleClickMs = ReadInt(txt, "shuttleDoubleClickMs", shuttleDoubleClickMs, 50);
                shuttleSensitivity = ReadFloat(txt, "shuttleSensitivity", shuttleSensitivity, 1f);
                shuttleDeadzone = ReadFloat(txt, "shuttleDeadzone", shuttleDeadzone, 0f);
                shuttleMidi14Bit = ReadBool(txt, "shuttleMidi14Bit", shuttleMidi14Bit);
            }
        } catch {}
    }

    private void LoadConfig() {
        LoadConfigFromFile(Path.Combine(baseDir, "config.json"));
        LoadConfigFromFile(configPath);
    }

    private void SaveConfig() {
        try {
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            string json = "{\r\n" +
                "  \"slowSpeed\": " + slowSpeed.ToString(inv) + ",\r\n" +
                "  \"jogSpeed\": " + jogSpeed.ToString(inv) + ",\r\n" +
                "  \"scrlSpeed\": " + scrlSpeed.ToString(inv) + ",\r\n" +
                "  \"invertWheel\": " + (invertWheel ? "true" : "false") + ",\r\n" +
                "  \"jogCurveEnabled\": " + (jogCurveEnabled ? "true" : "false") + ",\r\n" +
                "  \"jogCurveThreshold\": " + jogCurveThreshold.ToString(inv) + ",\r\n" +
                "  \"jogCurveRate\": " + jogCurveRate.ToString(inv) + ",\r\n" +
                "  \"jogCurveMaxMultiplier\": " + jogCurveMaxMultiplier.ToString(inv) + ",\r\n" +
                "  \"shuttleDoubleClickMs\": " + shuttleDoubleClickMs.ToString(inv) + ",\r\n" +
                "  \"shuttleSensitivity\": " + shuttleSensitivity.ToString(inv) + ",\r\n" +
                "  \"shuttleDeadzone\": " + shuttleDeadzone.ToString(inv) + ",\r\n" +
                "  \"shuttleMidi14Bit\": " + (shuttleMidi14Bit ? "true" : "false") + "\r\n}";
            File.WriteAllText(configPath, json);
        } catch {}
    }

    private static decimal ClampDecimal(decimal val, decimal min, decimal max) {
        if (val < min) return min;
        if (val > max) return max;
        return val;
    }

    private void OnOpenSettings(object sender, EventArgs e) {
        if (settingsForm != null && !settingsForm.IsDisposed) {
            settingsForm.Activate();
            return;
        }
        settingsForm = new Form();
        settingsForm.Text = "Replay Editor - Wheel Speed";
        settingsForm.ClientSize = new Size(410, 408);
        settingsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
        settingsForm.MaximizeBox = false;
        settingsForm.MinimizeBox = false;
        settingsForm.StartPosition = FormStartPosition.CenterScreen;

        Label l1 = new Label() { Text = "SLOW JOG Speed (fine):", Left = 20, Top = 23, Width = 240 };
        NumericUpDown n1 = new NumericUpDown() { Left = 270, Top = 20, Width = 115, Minimum = 0.1M, Maximum = 1000M, DecimalPlaces = 1, Increment = 0.5M, Value = ClampDecimal((decimal)slowSpeed, 0.1M, 1000M) };

        Label l2 = new Label() { Text = "JOG Speed (main 1:1):", Left = 20, Top = 57, Width = 240 };
        NumericUpDown n2 = new NumericUpDown() { Left = 270, Top = 54, Width = 115, Minimum = 0.1M, Maximum = 1000M, DecimalPlaces = 1, Increment = 0.5M, Value = ClampDecimal((decimal)jogSpeed, 0.1M, 1000M) };

        Label l3 = new Label() { Text = "SCRL Speed (fast scroll):", Left = 20, Top = 91, Width = 240 };
        NumericUpDown n3 = new NumericUpDown() { Left = 270, Top = 88, Width = 115, Minimum = 0.1M, Maximum = 10000M, DecimalPlaces = 1, Increment = 1M, Value = ClampDecimal((decimal)scrlSpeed, 0.1M, 10000M) };

        CheckBox cbCurve = new CheckBox() { Text = "Enable JOG Velocity Curve (Exponential)", Left = 20, Top = 124, Width = 365, Checked = jogCurveEnabled };

        Label l4 = new Label() { Text = "JOG Curve Threshold (ticks/s):", Left = 20, Top = 157, Width = 240 };
        NumericUpDown nThreshold = new NumericUpDown() { Left = 270, Top = 154, Width = 115, Minimum = 0M, Maximum = 500M, DecimalPlaces = 1, Increment = 5M, Value = ClampDecimal((decimal)jogCurveThreshold, 0M, 500M) };

        Label l5 = new Label() { Text = "JOG Curve Exp Rate:", Left = 20, Top = 191, Width = 240 };
        NumericUpDown nRate = new NumericUpDown() { Left = 270, Top = 188, Width = 115, Minimum = 0.000M, Maximum = 0.500M, DecimalPlaces = 3, Increment = 0.002M, Value = ClampDecimal((decimal)jogCurveRate, 0.000M, 0.500M) };

        Label l6 = new Label() { Text = "JOG Curve Max Multiplier:", Left = 20, Top = 225, Width = 240 };
        NumericUpDown nMaxMult = new NumericUpDown() { Left = 270, Top = 222, Width = 115, Minimum = 1M, Maximum = 100M, DecimalPlaces = 1, Increment = 1M, Value = ClampDecimal((decimal)jogCurveMaxMultiplier, 1M, 100M) };

        Label l7 = new Label() { Text = "SHTL Sensitivity (ticks for 100%):", Left = 20, Top = 259, Width = 240 };
        NumericUpDown nShuttleSens = new NumericUpDown() { Left = 270, Top = 256, Width = 115, Minimum = 1M, Maximum = 1000M, DecimalPlaces = 0, Increment = 5M, Value = ClampDecimal((decimal)shuttleSensitivity, 1M, 1000M) };

        CheckBox cbInv = new CheckBox() { Text = "Invert Wheel Direction (Invert Jog/Shuttle)", Left = 20, Top = 292, Width = 365, Checked = invertWheel };
        CheckBox cbMidi14 = new CheckBox() { Text = "14-bit Shuttle MIDI (CC2 + CC34, 0..16383)", Left = 20, Top = 320, Width = 365, Checked = shuttleMidi14Bit };

        Button btnSave = new Button() { Text = "Save and Apply", Left = 130, Top = 356, Width = 150, Height = 32 };
        btnSave.Click += (s, ev) => {
            slowSpeed = (float)n1.Value;
            jogSpeed = (float)n2.Value;
            scrlSpeed = (float)n3.Value;
            jogCurveEnabled = cbCurve.Checked;
            jogCurveThreshold = (float)nThreshold.Value;
            jogCurveRate = (float)nRate.Value;
            jogCurveMaxMultiplier = (float)nMaxMult.Value;
            shuttleSensitivity = (float)nShuttleSens.Value;
            invertWheel = cbInv.Checked;
            invertWheelItem.Checked = invertWheel;
            shuttleMidi14Bit = cbMidi14.Checked;
            SaveConfig();
            settingsForm.Close();
        };

        settingsForm.Controls.AddRange(new Control[] {
            l1, n1, l2, n2, l3, n3, cbCurve,
            l4, nThreshold, l5, nRate, l6, nMaxMult,
            l7, nShuttleSens, cbInv, cbMidi14, btnSave
        });
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

    private void UpdateLastKey(string text) {
        try {
            if (hiddenForm != null && hiddenForm.InvokeRequired) {
                hiddenForm.BeginInvoke(new Action(delegate { UpdateLastKey(text); }));
                return;
            }
            lastKeyItem.Text = text;
        } catch {}
    }

    private bool EnsureMidiOpen() {
        if (midiHandle != IntPtr.Zero || virtualMidiPort != IntPtr.Zero) return true;
        uint outCount = midiOutGetNumDevs();
        uint inCount = midiInGetNumDevs();
        try {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoftLabReplayBridge");
            if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);
            using (StreamWriter sw = new StreamWriter(Path.Combine(appDataPath, "midi_debug.txt"), false)) {
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
                        port = virtualMIDICreatePortEx2_64("SoftLab ReplayBridge", IntPtr.Zero, IntPtr.Zero, 65535, 1);
                    else
                        port = virtualMIDICreatePortEx2_32("SoftLab ReplayBridge", IntPtr.Zero, IntPtr.Zero, 65535, 1);
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
        lock (midiLock) {
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
    }

    private void SendShuttleMidi(double shuttle) {
        if (double.IsNaN(shuttle) || double.IsInfinity(shuttle)) shuttle = 0.0;
        if (shuttle < -1.0) shuttle = -1.0;
        if (shuttle > 1.0) shuttle = 1.0;

        // Handle0 in SoftLab_ReplayEditor.reg: Id = 2 ("MIDIHandle_JogWheel")
        if (shuttleMidi14Bit) {
            int midi14 = shuttle >= 0.0
                ? 8192 + (int)Math.Round(shuttle * 8191.0)
                : 8192 + (int)Math.Round(shuttle * 8192.0);
            if (midi14 < 0) midi14 = 0;
            if (midi14 > 16383) midi14 = 16383;
            uint msb = (uint)((midi14 >> 7) & 0x7F);
            uint lsb = (uint)(midi14 & 0x7F);
            SendMidi(0xB0u | (2u << 8) | (msb << 16));
            SendMidi(0xB0u | (34u << 8) | (lsb << 16));
        } else {
            int midi7 = shuttle >= 0.0
                ? 64 + (int)Math.Round(shuttle * 63.0)
                : 64 + (int)Math.Round(shuttle * 64.0);
            if (midi7 < 0) midi7 = 0;
            if (midi7 > 127) midi7 = 127;
            SendMidi(0xB0u | (2u << 8) | ((uint)midi7 << 16));
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
                        int steps;
                        if (!int.TryParse(p[1], out steps)) continue;
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
                    } else if (p[0] == "SHUTTLE") {
                        double shuttle;
                        if (double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out shuttle)) {
                            if (invertWheel) shuttle = -shuttle;
                            shuttleActive = Math.Abs(shuttle) > 1e-6;
                            SendShuttleMidi(shuttle);
                        }
                    } else if (p[0] == "ON") {
                        int note;
                        if (!int.TryParse(p[1], out note)) continue;
                        SendMidi(0x90u | ((uint)(note & 0x7F) << 8) | (127u << 16));
                        if (p.Length >= 3) UpdateLastKey("Last key: MIDIKey_" + p[2].Replace("-", "_") + "_DOWN");
                    } else if (p[0] == "OFF") {
                        int note;
                        if (!int.TryParse(p[1], out note)) continue;
                        SendMidi(0x80u | ((uint)(note & 0x7F) << 8));
                    } else if (p[0] == "CC" && p.Length >= 3) {
                        int cc;
                        int val;
                        if (!int.TryParse(p[1], out cc) || !int.TryParse(p[2], out val)) continue;
                        SendMidi(0xB0u | ((uint)(cc & 0x7F) << 8) | ((uint)(val & 0x7F) << 16));
                    } else if (p[0] == "STATUS") {
                        if (p[1] == "READY") UpdateStatus("Pripojeno (Dvojklik = Nastaveni)", Color.LimeGreen);
                        else if (p[1] == "WAIT_USB") UpdateStatus("Cekam na pripojeni USB pultu...", Color.Orange);
                        else if (p[1] == "BUSY") UpdateStatus("Pult je blokovan (vypni Companion/Resolve)", Color.Red);
                        else UpdateStatus("Odpojeno, zkusim znovu...", Color.Orange);
                    }
                }
            } catch {}
            if (shuttleActive) {
                shuttleActive = false;
                SendShuttleMidi(0.0);
            }
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
        if (shuttleActive) {
            shuttleActive = false;
            SendShuttleMidi(0.0);
        }
        try { if (nodeProc != null && !nodeProc.HasExited) nodeProc.Kill(); } catch {}
    }

    private void OnExit(object sender, EventArgs e) {
        running = false;
        if (shuttleActive) {
            shuttleActive = false;
            SendShuttleMidi(0.0);
        }
        try { if (nodeProc != null && !nodeProc.HasExited) nodeProc.Kill(); } catch {}
        lock (midiLock) {
            if (midiHandle != IntPtr.Zero) { midiOutClose(midiHandle); midiHandle = IntPtr.Zero; }
            if (virtualMidiPort != IntPtr.Zero) {
                try {
                    if (is64bit) virtualMIDIClosePort_64(virtualMidiPort);
                    else virtualMIDIClosePort_32(virtualMidiPort);
                } catch {}
                virtualMidiPort = IntPtr.Zero;
            }
        }
        trayIcon.Visible = false;
        Application.Exit();
    }

    static string crashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "SoftLabReplayBridge", "crash.txt");

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