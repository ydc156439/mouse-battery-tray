using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseBattery
{
    internal static class Store
    {
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseBatteryTrayNative");
        public static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.txt");
        private static readonly object LogLock = new object();
        public static void Log(string text)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    string path = Path.Combine(DirectoryPath, "monitor.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 512000) File.WriteAllText(path, "");
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + text + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon[] icons = new NotifyIcon[2];
        private readonly Icon[] images = new Icon[2];
        private Reading[] readings = { Reading.Unknown("正在检测"), Reading.Unknown("正在检测") };
        private readonly DateTime[] alerts = { DateTime.MinValue, DateTime.MinValue };
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly object gate = new object();
        private Reading[] pending;
        private bool busy, stopping;
        private int interval = 60, threshold = 20;
        private bool notifications = true;
        private DateTime next = DateTime.MinValue, updated;
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private Dashboard dashboard;
        private readonly EventWaitHandle showSignal;
        internal int RefreshSeconds { get { return interval; } }
        internal int AlertThreshold { get { return threshold; } }
        internal bool NotificationsEnabled { get { return notifications; } }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        public TrayContext(bool showPanel, EventWaitHandle signal)
        {
            showSignal = signal;
            LoadSettings();
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                icons[i] = new NotifyIcon();
                images[i] = Draw(readings[i], i); icons[i].Icon = images[i];
                icons[i].Text = Protocol.Models[i];
                icons[i].ContextMenuStrip = MenuFor(index);
                icons[i].DoubleClick += delegate { Details(); };
                icons[i].Visible = true;
            }
            timer.Interval = 250; timer.Tick += Tick; timer.Start();
            if (showPanel) Details();
        }

        private Icon Draw(Reading reading, int index)
        {
            using (Bitmap bitmap = new Bitmap(64, 64))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font small = new Font("Segoe UI", 9, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font font = new Font("Segoe UI", reading.Percent == 100 ? 35 : 43, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.Transparent); graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Color color = reading.Charging ? Color.FromArgb(66, 214, 164) :
                    (reading.Percent.HasValue && reading.Percent.Value <= threshold ? Color.FromArgb(255, 107, 119) : TrayTextColor());
                using (Brush brush = new SolidBrush(color))
                using (Brush label = new SolidBrush(Color.FromArgb(112, 184, 255)))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center; format.LineAlignment = StringAlignment.Center;
                    graphics.DrawString(reading.Percent.HasValue ? reading.Percent.Value.ToString() : "--", font, brush,
                        new RectangleF(0, 8, 64, 48), format);
                    graphics.DrawString(index == 0 ? "S" : "A", small, label, 3, 0);
                    if (reading.Charging) graphics.DrawString("+", small, brush, 53, 0);
                    graphics.FillRectangle(Brushes.SlateGray, 4, 59, 56, 4);
                    if (reading.Percent.HasValue) graphics.FillRectangle(brush, 4, 59, 56 * reading.Percent.Value / 100f, 4);
                }
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        private ToolStripMenuItem Item(string text, EventHandler action)
        { ToolStripMenuItem item = new ToolStripMenuItem(text); if (action != null) item.Click += action; return item; }

        private ContextMenuStrip MenuFor(int index)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(Item("打开电量面板", delegate { Details(); }));
            ToolStripMenuItem status = Item("正在检测", null); status.Enabled = false; menu.Items.Add(status);
            menu.Items.Add(Item("立即刷新", delegate { next = DateTime.MinValue; }));
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem rates = Item("刷新间隔", null);
            foreach (int seconds in new int[] { 30, 60, 120, 300 })
            {
                int value = seconds;
                ToolStripMenuItem choice = Item(seconds < 60 ? "30 秒" : seconds / 60 + " 分钟", delegate { interval = value; SaveSettings(); next = DateTime.MinValue; });
                choice.Tag = seconds; rates.DropDownItems.Add(choice);
            }
            menu.Items.Add(rates);
            ToolStripMenuItem thresholds = Item("低电量阈值", null);
            foreach (int level in new int[] { 10, 15, 20, 30 })
            {
                int value = level;
                ToolStripMenuItem choice = Item(level + "%", delegate { threshold = value; SaveSettings(); next = DateTime.MinValue; });
                choice.Tag = level; thresholds.DropDownItems.Add(choice);
            }
            menu.Items.Add(thresholds);
            ToolStripMenuItem notify = Item("低电量提醒", delegate { notifications = !notifications; SaveSettings(); }); menu.Items.Add(notify);
            ToolStripMenuItem startup = Item("开机启动", delegate
            {
                try
                {
                    bool enabled = StartupEnabled();
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKey))
                    { if (enabled) key.DeleteValue("MouseBatteryTrayNative", false); else key.SetValue("MouseBatteryTrayNative", StartupCommand()); }
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "开机启动设置失败"); }
                if (dashboard != null && !dashboard.IsDisposed) dashboard.SyncSettings();
            }); menu.Items.Add(startup);
            menu.Items.Add(Item("导出设备诊断", delegate { Export(); }));
            menu.Items.Add(Item("打开日志目录", delegate { OpenDirectory(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("退出", delegate { ExitThread(); }));
            menu.Opening += delegate
            {
                status.Text = readings[index].ToString(); notify.Checked = notifications; startup.Checked = StartupEnabled();
                foreach (ToolStripMenuItem choice in rates.DropDownItems) choice.Checked = (int)choice.Tag == interval;
                foreach (ToolStripMenuItem choice in thresholds.DropDownItems) choice.Checked = (int)choice.Tag == threshold;
            };
            return menu;
        }

        private static string StartupCommand() { return "\"" + Application.ExecutablePath + "\" --background"; }
        private static Color TrayTextColor()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    if (key != null && Convert.ToInt32(key.GetValue("SystemUsesLightTheme", 0)) == 1) return Color.FromArgb(32, 43, 59);
            }
            catch { }
            return Color.White;
        }
        internal static bool StartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey))
                {
                    string value = key == null ? null : key.GetValue("MouseBatteryTrayNative") as string;
                    return value == StartupCommand() || value == "\"" + Application.ExecutablePath + "\"";
                }
            }
            catch { return false; }
        }

        internal void RequestRefresh() { next = DateTime.MinValue; }
        internal void Diagnose() { Export(); }
        internal void Configure(int seconds, int low, bool notify, bool autostart)
        {
            interval = seconds; threshold = low; notifications = notify;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKey))
                {
                    if (autostart) key.SetValue("MouseBatteryTrayNative", StartupCommand());
                    else if (StartupEnabled()) key.DeleteValue("MouseBatteryTrayNative", false);
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "开机启动设置失败"); }
            SaveSettings(); RequestRefresh();
        }

        private void Tick(object sender, EventArgs args)
        {
            if (showSignal.WaitOne(0)) Details();
            Reading[] values = null;
            lock (gate)
            {
                if (pending != null) { values = pending; pending = null; }
            }
            if (values != null) Apply(values);
            if (dashboard != null && dashboard.Visible) dashboard.UpdateStatus(readings, updated, busy);
            lock (gate)
            {
                if (busy || stopping || DateTime.UtcNow < next) return;
                busy = true; next = DateTime.UtcNow.AddSeconds(interval);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                Reading[] result;
                try { result = Hid.Scan(); }
                catch (Exception ex) { Store.Log(ex.ToString()); result = new Reading[] { Reading.Unknown("设备检测失败"), Reading.Unknown("设备检测失败") }; }
                lock (gate) { if (!stopping) pending = result; busy = false; }
            });
        }

        private void Apply(Reading[] values)
        {
            readings = values; updated = DateTime.Now;
            for (int i = 0; i < 2; i++)
            {
                Icon previous = images[i]; images[i] = Draw(values[i], i); icons[i].Icon = images[i]; previous.Dispose();
                string title = Protocol.Models[i] + " · " + values[i]; icons[i].Text = title.Length > 63 ? title.Substring(0, 63) : title;
                Reading reading = values[i];
                if (!reading.Percent.HasValue) continue;
                if (reading.Charging || reading.Percent.Value > threshold) { alerts[i] = DateTime.MinValue; continue; }
                if (notifications && (DateTime.UtcNow - alerts[i]).TotalHours >= 1)
                {
                    icons[i].ShowBalloonTip(5000, Protocol.Models[i] + " 电量不足", "剩余电量 " + reading.Percent + "% ，请及时充电。", ToolTipIcon.Warning);
                    alerts[i] = DateTime.UtcNow;
                }
            }
        }

        private void Details()
        {
            if (dashboard == null || dashboard.IsDisposed) dashboard = new Dashboard(this);
            dashboard.SyncSettings(); dashboard.UpdateStatus(readings, updated, busy);
            dashboard.Show(); if (dashboard.WindowState == FormWindowState.Minimized) dashboard.WindowState = FormWindowState.Normal;
            dashboard.Activate();
        }

        private static void OpenDirectory()
        { try { Directory.CreateDirectory(Store.DirectoryPath); System.Diagnostics.Process.Start(Store.DirectoryPath); } catch (Exception ex) { MessageBox.Show(ex.Message); } }

        private void Export()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    StringBuilder report = new StringBuilder(); report.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    report.AppendLine("MouseBatteryTray native / OS " + Environment.OSVersion + " / " + (IntPtr.Size * 8) + " bit");
                    report.AppendLine("Only Ninjutso/ATK vendors are listed; no serial numbers or device paths.");
                    foreach (Device device in Hid.Enumerate()) report.AppendLine(device.Summary + " model=" + device.Model);
                    Directory.CreateDirectory(Store.DirectoryPath);
                    File.WriteAllText(Path.Combine(Store.DirectoryPath, "diagnostics.txt"), report.ToString(), Encoding.UTF8);
                    OpenDirectory();
                }
                catch (Exception ex) { Store.Log(ex.ToString()); MessageBox.Show(ex.Message, "诊断导出失败"); }
            });
        }

        private void LoadSettings()
        {
            try
            {
                string[] settings = File.ReadAllLines(Store.SettingsPath); int value;
                if (settings.Length >= 3)
                {
                    if (Int32.TryParse(settings[0], out value) && Array.IndexOf(new int[] { 30, 60, 120, 300 }, value) >= 0) interval = value;
                    if (Int32.TryParse(settings[1], out value) && Array.IndexOf(new int[] { 10, 15, 20, 30 }, value) >= 0) threshold = value;
                    notifications = settings[2] != "0";
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private void SaveSettings()
        {
            try { Directory.CreateDirectory(Store.DirectoryPath); File.WriteAllLines(Store.SettingsPath, new string[] { interval.ToString(), threshold.ToString(), notifications ? "1" : "0" }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "设置保存失败"); }
            if (dashboard != null && !dashboard.IsDisposed) dashboard.SyncSettings();
        }

        protected override void ExitThreadCore()
        {
            lock (gate) stopping = true;
            timer.Stop(); timer.Dispose();
            if (dashboard != null) dashboard.Dispose();
            for (int i = 0; i < 2; i++) { icons[i].Visible = false; icons[i].ContextMenuStrip.Dispose(); icons[i].Dispose(); images[i].Dispose(); }
            base.ExitThreadCore();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\MouseBatteryTrayNative", out created))
            {
                if (!created)
                {
                    try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting(@"Local\MouseBatteryTrayNative.ShowPanel")) signal.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("程序正在启动，请稍后重试。", "鼠标电量"); }
                    return;
                }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Store.Log(e.Exception.ToString()); MessageBox.Show(e.Exception.Message, "鼠标电量监控"); };
                try
                {
                    using (EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MouseBatteryTrayNative.ShowPanel"))
                        Application.Run(new TrayContext(Array.IndexOf(Environment.GetCommandLineArgs(), "--background") < 0, signal));
                }
                catch (Exception ex) { Store.Log(ex.ToString()); MessageBox.Show(ex.Message, "鼠标电量监控启动失败"); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
