using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Windows.Automation;
using ScreenForge.Capture;
using CaptureTesting;

namespace CaptureProbe;

/// <summary>Gerçek masaüstünde BitBlt tazeliğini ve kısayoldan görünür piksele gecikmeyi ölçer.</summary>
internal static class Program
{
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeout(nint hwnd, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    private delegate bool EnumProc(nint hwnd, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }

    private static readonly Color[] Colors = [Color.FromArgb(224, 96, 64), Color.FromArgb(64, 208, 144), Color.FromArgb(96, 128, 240)];

    [STAThread]
    private static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new nint(-4));
        DesktopStimulus.SetThreadDpiAwarenessContext(new nint(-4));
        string output = Path.GetFullPath(args.Length > 1 ? args[1] : "capture-probe.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string settingsPath = ScreenForge.Settings.AppSettings.SettingsPath;
        byte[]? originalSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        bool settingsChanged = false;
        using var startupKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        object? originalStartup = startupKey?.GetValue("ScreenForge");
        var originalStartupKind = originalStartup == null ? Microsoft.Win32.RegistryValueKind.String : startupKey!.GetValueKind("ScreenForge");
        Process? app = null;
        Func<bool, Bitmap> captureScreen = ScreenCapture.CaptureVirtualScreen;
        bool freshnessOnly = args.Length == 0 || args[0] == "--freshness";
        string captureAssembly = freshnessOnly
            ? args.Length > 2 ? Path.GetFullPath(args[2]) : typeof(ScreenCapture).Assembly.Location
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "ScreenForge.dll");
        // Önce ölçümünde de gerçekten önceki yakalama uygulamasını kullan.
        var context = new AssemblyLoadContext("capture-probe-target", isCollectible: true);
        context.Resolving += (_, name) =>
        {
            string candidate = Path.Combine(Path.GetDirectoryName(captureAssembly)!, name.Name + ".dll");
            return name.Name!.StartsWith("System.") || !File.Exists(candidate) ? null : context.LoadFromAssemblyPath(candidate);
        };
        var captureType = context.LoadFromAssemblyPath(captureAssembly).GetType("ScreenForge.Capture.ScreenCapture")!;
        captureScreen = captureType.GetMethod("CaptureVirtualScreen")!.CreateDelegate<Func<bool, Bitmap>>();
        var trials = new List<object>();
        var latency = new List<object>();
        var bounds = ScreenCapture.VirtualScreenBounds;
        int fresh = 0;
        bool copied = false, saved = false;
        bool overlayFresh = true;
        double? inputIdleMs = null;
        double? firstInputSinceStartMs = null;
        double? hotkeyReadyMs = null;
        string? savedPath = null;
        try
        {
            if (Process.GetProcessesByName("ScreenForge").Length != 0)
                throw new InvalidOperationException("Ölçüm için çalışan ScreenForge'u önce kapatın.");
            using var stimulus = new DesktopStimulus();
            Pump(100);
            var sample = new Point(stimulus.Left + 240, stimulus.Top + 170);
            for (int i = 0; i < 20; i++)
            {
                var color = Colors[i % Colors.Length];
                Paint(stimulus, color);
                var timer = Stopwatch.StartNew();
                using var capture = captureScreen(false);
                double ms = timer.Elapsed.TotalMilliseconds;
                var actual = capture.GetPixel(sample.X - bounds.X, sample.Y - bounds.Y);
                bool ok = Same(actual, color);
                if (ok) fresh++;
                trials.Add(new { trial = i + 1, expected = color.ToArgb(), actual = actual.ToArgb(), ok, captureMs = ms });
            }

            if (args.Length > 0 && args[0] != "--freshness")
            {
                // Gerçek uygulama başlangıcı; kişisel ayarlar finally içinde bayt bayt geri alınır.
                var config = originalSettings == null ? new JsonObject() : JsonNode.Parse(originalSettings)!.AsObject();
                config["RegionHotkey"] = new JsonObject { ["Key"] = "S", ["Modifiers"] = "Control, Shift" };
                config["LaunchAtStartup"] = false;
                config["ShowCursor"] = false;
                config["OutputFormat"] = "Png";
                config["SaveDirectory"] = Path.GetDirectoryName(output);
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                File.WriteAllText(settingsPath, config.ToJsonString());
                settingsChanged = true;
                var startupClock = Stopwatch.StartNew();
                app = Process.Start(new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
                app.OutputDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine("app: " + e.Data); };
                app.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine("app error: " + e.Data); };
                app.BeginOutputReadLine();
                app.BeginErrorReadLine();
                // İlk kısayol kayıt edilmeden tuş göndermek gecikme ölçümü değildir.
                // Sabit uyku yerine başlangıç mesaj kuyruğunun hazır olmasını bekle.
                if (!app.WaitForInputIdle(10000))
                    throw new InvalidOperationException("Uygulama giriş kuyruğu hazır olmadı.");
                inputIdleMs = startupClock.Elapsed.TotalMilliseconds;
                // WPF HWND kurulumu da input-idle bildirebilir; uygulamaya ait
                // kısayol penceresini ve UI mesaj yanıtını ayrıca doğrula.
                nint hotkeyWindow = 0;
                while (startupClock.ElapsedMilliseconds < 10000)
                {
                    hotkeyWindow = FindWindowEx(new nint(-3), 0, null, "ScreenForgeHotkeyWindow");
                    if (hotkeyWindow != 0)
                    {
                        GetWindowThreadProcessId(hotkeyWindow, out uint owner);
                        if (owner == app.Id && SendMessageTimeout(hotkeyWindow, 0, 0, 0, 2, 1000, out _) != 0) break;
                    }
                    hotkeyWindow = 0;
                    Pump(1);
                }
                if (hotkeyWindow == 0) throw new InvalidOperationException("Kısayol penceresi hazır olmadı.");
                hotkeyReadyMs = startupClock.Elapsed.TotalMilliseconds;
                // Ölçüm saatine aracın kendi ilk LINQ/JIT/interop maliyetini katma.
                var hotkeyInputs = KeyInputs(0x11, 0x10, 0x53);
                int inputSize = Marshal.SizeOf<INPUT>();
                SendInput(0, hotkeyInputs, inputSize);
                // Başlangıç ısınması için sabit süre; aynı süre önce/sonra kullanılır.
                int startupDelay = args.Length > 2 ? int.Parse(args[2]) : 2000;
                Pump(startupDelay);
                const int presses = 20;
                for (int i = 0; i < presses; i++)
                {
                    var color = Colors[(i + 1) % Colors.Length];
                    SetForegroundWindow(stimulus.Handle);
                    Paint(stimulus, color);
                    SetCursorPos(900, 700);
                    uint raw = Rgb(color), dim = Rgb(Color.FromArgb((int)Math.Round(color.R * 133.0 / 255), (int)Math.Round(color.G * 133.0 / 255), (int)Math.Round(color.B * 133.0 / 255)));
                    nint dc = GetDC(0);
                    double firstChange = -1, correct = -1;
                    uint firstPixel = raw;
                    // Tazelik deneyi yukarıda beklemesizdir. Gecikme deneyinde ise
                    // uyarıcının ekranda sunulduğunu doğrulamadan saati başlatma;
                    // önceki masaüstü karesi overlay'in ilk karesi sanılmasın.
                    var ready = Stopwatch.StartNew();
                    while (!Near(GetPixel(dc, sample.X, sample.Y), raw) && ready.ElapsedMilliseconds < 1000) Pump(1);
                    if (!Near(GetPixel(dc, sample.X, sample.Y), raw))
                    {
                        ReleaseDC(0, dc);
                        throw new InvalidOperationException("Gecikme uyarıcısı ekranda sunulmadı.");
                    }
                    if (i == 0) firstInputSinceStartMs = startupClock.Elapsed.TotalMilliseconds;
                    var clock = Stopwatch.StartNew();
                    long started = Stopwatch.GetTimestamp();
                    double sent = 0;
                    try
                    {
                        if (SendInput((uint)hotkeyInputs.Length, hotkeyInputs, inputSize) != hotkeyInputs.Length)
                            throw new InvalidOperationException("Kısayol gönderilemedi.");
                        sent = clock.Elapsed.TotalMilliseconds;
                        while (clock.ElapsedMilliseconds < 5000)
                        {
                            uint pixel = GetPixel(dc, sample.X, sample.Y);
                            if (firstChange < 0 && !Near(pixel, raw)) { firstChange = clock.Elapsed.TotalMilliseconds; firstPixel = pixel; }
                            if (Near(pixel, dim)) { correct = clock.Elapsed.TotalMilliseconds; break; }
                            DesktopStimulus.Pump();
                            Thread.Sleep(1);
                        }
                    }
                    finally { ReleaseDC(0, dc); }
                    latency.Add(new { trial = i + 1, first = i == 0, started, sendInputMs = sent, firstChangeMs = firstChange, correctFrozenPixelMs = correct, firstPixel, expectedDim = dim, firstFrameFresh = Near(firstPixel, dim) });
                    overlayFresh &= Near(firstPixel, dim);
                    if (correct < 0) throw new InvalidOperationException($"{i + 1}. basışta güncel donmuş piksel görünmedi.");
                    Pump(180);
                    if (i == presses - 2 || i == presses - 1)
                    {
                        Drag(stimulus.Left + 70, stimulus.Top + 70, stimulus.Left + 380, stimulus.Top + 270);
                        Pump(250);
                        if (i == presses - 2)
                        {
                            Chord(0x11, 0x43);
                            Pump(350);
                            var image = System.Windows.Clipboard.GetImage() ?? throw new InvalidOperationException("Panoda görüntü yok.");
                            var data = new byte[4];
                            image.CopyPixels(new System.Windows.Int32Rect(image.PixelWidth / 2, image.PixelHeight / 2, 1, 1), data, 4, 0);
                            copied = Same(Color.FromArgb(data[2], data[1], data[0]), color);
                        }
                        else
                        {
                            string png = savedPath = Path.Combine(Path.GetDirectoryName(output)!, $"selection-{Guid.NewGuid():N}.png");
                            Chord(0x11, 0x53);
                            var dialogClock = Stopwatch.StartNew();
                            AutomationElement? dialog = null;
                            while (dialog == null && dialogClock.ElapsedMilliseconds < 5000)
                            {
                                dialog = AutomationElement.RootElement.FindFirst(TreeScope.Descendants,
                                    new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id),
                                        new PropertyCondition(AutomationElement.ClassNameProperty, "#32770")));
                                Pump(20);
                            }
                            if (dialog == null)
                            {
                                var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id));
                                string details = string.Join("; ", windows.Cast<AutomationElement>().Select(w => $"{w.Current.ClassName}: {w.Current.Name}"));
                                foreach (AutomationElement window in windows)
                                {
                                    var text = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                                    details += " | " + string.Join("; ", text.Cast<AutomationElement>().Select(t => t.Current.Name));
                                }
                                throw new InvalidOperationException("Kaydet iletişim kutusu açılmadı. " + details);
                            }
                            var edit = dialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1001"));
                            if (edit == null) throw new InvalidOperationException("Dosya adı alanı bulunamadı.");
                            edit.SetFocus();
                            ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(png);
                            Pump(50);
                            Chord(0x0D);
                            var fileClock = Stopwatch.StartNew();
                            while (!File.Exists(png) && fileClock.ElapsedMilliseconds < 5000) Pump(20);
                            if (!File.Exists(png))
                            {
                                var text = dialog.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                                throw new InvalidOperationException("Kaydetme yeni PNG dosyasını oluşturmadı: " + png + " | " + string.Join("; ", text.Cast<AutomationElement>().Select(t => t.Current.Name)));
                            }
                            Pump(150);
                            using var image = new Bitmap(png);
                            saved = Same(image.GetPixel(image.Width / 2, image.Height / 2), color);
                        }
                    }
                    else Chord(0x1B);
                    Pump(700);
                    var closeClock = Stopwatch.StartNew();
                    while (VisibleOverlay(app.Id) && closeClock.ElapsedMilliseconds < 2000) Pump(20);
                    if (VisibleOverlay(app.Id)) throw new InvalidOperationException("Overlay kapanmadı.");
                }
            }
            Write(null);
            return fresh == 20 && (args.Length == 0 || args[0] == "--freshness" || (overlayFresh && copied && saved)) ? 0 : 1;
        }
        catch (Exception ex) { Write(ex.ToString()); Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(); }
            if (settingsChanged)
            {
                if (originalSettings != null) File.WriteAllBytes(settingsPath, originalSettings);
                else File.Delete(settingsPath);
                if (originalStartup != null) startupKey?.SetValue("ScreenForge", originalStartup, originalStartupKind);
                else startupKey?.DeleteValue("ScreenForge", throwOnMissingValue: false);
            }
        }

        void Write(string? error)
        {
            var result = new { utc = DateTime.UtcNow, runtime = Environment.Version.ToString(), captureAssembly, assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(captureAssembly))), startupDelayMs = freshnessOnly ? 0 : args.Length > 2 ? int.Parse(args[2]) : 2000, inputIdleMs, hotkeyReadyMs, firstInputSinceStartMs, bounds = new { bounds.X, bounds.Y, bounds.Width, bounds.Height }, freshnessPassed = fresh, freshnessTotal = 20, trials, latency, overlayFresh, copied, saved, savedPath, error };
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(output, json);
            Console.WriteLine(json);
        }
    }

    private static bool VisibleOverlay(int process)
    {
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int));
            if (pid == process && cloaked == 0 && IsWindowVisible(hwnd) && GetWindowRect(hwnd, out var r) && r.Left > -10000 && r.Right - r.Left > 1000) found = true;
            return true;
        }, 0);
        return found;
    }

    private static void Paint(DesktopStimulus form, Color color) => form.Present(color);
    private static bool Same(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;
    private static uint Rgb(Color c) => (uint)(c.R | c.G << 8 | c.B << 16);
    private static bool Near(uint a, uint b) => Enumerable.Range(0, 3).All(i => Math.Abs((int)((a >> (8 * i)) & 255) - (int)((b >> (8 * i)) & 255)) <= 2);
    private static void Pump(int ms) { var c = Stopwatch.StartNew(); while (c.ElapsedMilliseconds < ms) { DesktopStimulus.Pump(); Thread.Sleep(1); } }
    private static void Chord(params ushort[] keys)
    {
        var inputs = KeyInputs(keys);
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length) throw new InvalidOperationException("SendInput başarısız.");
    }
    private static INPUT[] KeyInputs(params ushort[] keys) => keys.Select(k => new INPUT { Type = 1, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { Key = k } } })
        .Concat(keys.Reverse().Select(k => new INPUT { Type = 1, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { Key = k, Flags = 2 } } })).ToArray();
    private static void Drag(int x1, int y1, int x2, int y2)
    {
        SetCursorPos(x1, y1);
        SendInput(1, [new INPUT { Data = new INPUTUNION { Mouse = new MOUSEINPUT { Flags = 2 } } }], Marshal.SizeOf<INPUT>());
        for (int i = 1; i <= 12; i++) { SetCursorPos(x1 + (x2 - x1) * i / 12, y1 + (y2 - y1) * i / 12); Pump(8); }
        SendInput(1, [new INPUT { Data = new INPUTUNION { Mouse = new MOUSEINPUT { Flags = 4 } } }], Marshal.SizeOf<INPUT>());
    }
}
