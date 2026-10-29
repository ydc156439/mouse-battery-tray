using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MouseBattery
{
    internal static class Palette
    {
        public static readonly Color Background = Color.FromArgb(15, 20, 29);
        public static readonly Color Surface = Color.FromArgb(24, 32, 44);
        public static readonly Color Border = Color.FromArgb(43, 55, 72);
        public static readonly Color Text = Color.FromArgb(235, 241, 250);
        public static readonly Color Muted = Color.FromArgb(148, 165, 186);
        public static readonly Color Mint = Color.FromArgb(103, 228, 188);
        public static readonly Color Blue = Color.FromArgb(129, 184, 255);
        public static readonly Color Red = Color.FromArgb(255, 133, 145);
        public static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
    }

    internal sealed class BatteryCard : Control
    {
        private readonly int model;
        private Reading reading = Reading.Unknown("正在检测");
        private int threshold = 20;
        public BatteryCard(int index)
        {
            model = index; Dock = DockStyle.Fill; Margin = new Padding(index == 0 ? 0 : 8, 0, index == 0 ? 8 : 0, 0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Background; AccessibleRole = AccessibleRole.Grouping;
        }
        public void UpdateReading(Reading value, int low)
        {
            if (Object.ReferenceEquals(reading, value) && threshold == low) return;
            reading = value; threshold = low; AccessibleName = Protocol.Models[model] + "，" + reading; Invalidate();
        }
        private static void TextAt(Graphics g, string text, float size, FontStyle style, Color color, RectangleF area)
        {
            using (Font font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            { format.Trimming = StringTrimming.EllipsisCharacter; format.FormatFlags = StringFormatFlags.NoWrap; g.DrawString(text, font, brush, area, format); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // Paint in a stable design coordinate system; the host scales with Windows DPI.
            float scale = Height / 224f; if (scale <= 0) return;
            float designWidth = Width / scale, trackWidth = designWidth - 44, mouseX = designWidth - 87;
            g.ScaleTransform(scale, scale);
            using (GraphicsPath outline = Palette.Rounded(new RectangleF(1, 1, designWidth - 2, 222), 16))
            using (Brush surface = new SolidBrush(Palette.Surface))
            using (Pen border = new Pen(Palette.Border))
            { g.FillPath(surface, outline); g.DrawPath(border, outline); }
            Color accent = model == 0 ? Palette.Mint : Palette.Blue;
            Color valueColor = reading.Charging ? Palette.Mint :
                (reading.Percent.HasValue && reading.Percent.Value <= threshold ? Palette.Red : Palette.Text);
            using (Brush marker = new SolidBrush(accent)) g.FillEllipse(marker, 22, 25, 7, 7);
            TextAt(g, model == 0 ? "NINJUTSO" : "ATK", 10, FontStyle.Bold, Palette.Muted, new RectangleF(36, 19, 200, 18));
            TextAt(g, Protocol.Models[model], 20, FontStyle.Bold, Palette.Text, new RectangleF(21, 43, 280, 32));
            bool connected = reading.Percent.HasValue;
            string state = !connected ? "等待连接" : (reading.Charging ? "正在充电" : reading.Percent <= threshold ? "电量偏低" : "连接正常");
            Color stateColor = !connected ? Palette.Muted : valueColor == Palette.Red ? Palette.Red : accent;
            using (Brush pill = new SolidBrush(Color.FromArgb(32, stateColor)))
            using (GraphicsPath path = Palette.Rounded(new RectangleF(22, 81, 92, 25), 12)) g.FillPath(pill, path);
            TextAt(g, state, 11, FontStyle.Regular, stateColor, new RectangleF(33, 85, 80, 20));
            TextAt(g, connected ? reading.Percent.Value.ToString() : "--", 54, FontStyle.Bold, valueColor, new RectangleF(17, 112, 147, 75));
            if (connected) TextAt(g, "%", 20, FontStyle.Regular, Palette.Muted,
                new RectangleF(reading.Percent == 100 ? 137 : reading.Percent >= 10 ? 104 : 73, 145, 35, 34));
            // A small code-drawn mouse keeps the EXE independent of image files.
            using (Pen pen = new Pen(Color.FromArgb(68, 87, 109), 2))
            using (GraphicsPath mouse = Palette.Rounded(new RectangleF(mouseX, 106, 53, 74), 25))
            {
                g.DrawPath(pen, mouse); g.DrawLine(pen, mouseX + 26, 107, mouseX + 26, 139);
                g.DrawLine(pen, mouseX + 3, 140, mouseX + 50, 140);
                using (Brush wheel = new SolidBrush(accent)) g.FillRectangle(wheel, mouseX + 24, 116, 4, 12);
            }
            using (Brush track = new SolidBrush(Palette.Border))
            using (GraphicsPath path = Palette.Rounded(new RectangleF(22, 187, trackWidth, 5), 2.5f)) g.FillPath(track, path);
            if (connected && reading.Percent.Value > 0)
                using (Brush fill = new SolidBrush(valueColor == Palette.Red ? Palette.Red : accent))
                    g.FillRectangle(fill, 22, 187, Math.Max(3, trackWidth * reading.Percent.Value / 100f), 5);
            string note = connected ? (reading.Charging ? "充电中 · 可随时查看电量" : "2.4 GHz / USB · " + reading.Status) : reading.Status;
            TextAt(g, note, 10, FontStyle.Regular, Palette.Muted, new RectangleF(22, 201, trackWidth, 18));
        }
    }

    internal sealed class Dashboard : Form
    {
        private readonly TrayContext host;
        private readonly BatteryCard[] cards = { new BatteryCard(0), new BatteryCard(1) };
        private readonly ComboBox rate = new ComboBox(), limit = new ComboBox();
        private readonly CheckBox notify = new CheckBox(), startup = new CheckBox();
        private readonly Label last = new Label();
        private readonly Button refresh;
        private readonly TableLayoutPanel deviceGrid = new TableLayoutPanel();
        private readonly Label emptyState = new Label();
        private int displayedMask = -1;
        private bool syncing;
        private readonly Font uiFont = new Font("Microsoft YaHei UI", 9f);

        public Dashboard(TrayContext owner)
        {
            host = owner; Text = "鼠标电量 v" + Application.ProductVersion + " · Mouse Energy";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Font = uiFont; BackColor = Palette.Background; ForeColor = Palette.Text;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(820, 610); MinimumSize = new Size(790, 640);
            StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false; DoubleBuffered = true;
            TableLayoutPanel layout = new TableLayoutPanel(); layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(28, 20, 28, 16); layout.ColumnCount = 1; layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            Controls.Add(layout);

            TableLayoutPanel header = new TableLayoutPanel(); header.Dock = DockStyle.Fill; header.Margin = Padding.Empty;
            header.ColumnCount = 2; header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            Panel title = new Panel(); title.Dock = DockStyle.Fill;
            Label heading = LabelFor("设备电量", 23, Palette.Text); heading.Font = new Font("Microsoft YaHei UI", 23, FontStyle.Bold);
            heading.Dock = DockStyle.None; heading.AutoSize = true; heading.Location = new Point(0, 0); title.Controls.Add(heading);
            Label subtitle = LabelFor("v" + Application.ProductVersion + " · 两只鼠标，一眼掌握。", 10, Palette.Muted); subtitle.Dock = DockStyle.None;
            subtitle.AutoSize = true; subtitle.Location = new Point(2, 49); title.Controls.Add(subtitle);
            header.Controls.Add(title, 0, 0);
            refresh = ButtonFor("立即刷新", true); refresh.Dock = DockStyle.Top; refresh.Height = 38; refresh.Margin = new Padding(0, 7, 0, 0);
            refresh.Click += delegate { host.RequestRefresh(); }; header.Controls.Add(refresh, 1, 0); layout.Controls.Add(header, 0, 0);

            deviceGrid.Dock = DockStyle.Fill;
            deviceGrid.Margin = Padding.Empty; deviceGrid.ColumnCount = 2;
            deviceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); deviceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            deviceGrid.Controls.Add(cards[0], 0, 0); deviceGrid.Controls.Add(cards[1], 1, 0); layout.Controls.Add(deviceGrid, 0, 1);
            emptyState.Dock = DockStyle.Fill; emptyState.TextAlign = ContentAlignment.MiddleCenter; emptyState.ForeColor = Palette.Muted;
            emptyState.Font = new Font("Microsoft YaHei UI", 12); emptyState.BackColor = Palette.Surface;
            emptyState.Text = "等待鼠标接收器\n\n插入 Sora V2 或 ATK X1 Pro Max 接收器后，设备会自动出现。";

            TableLayoutPanel settings = new TableLayoutPanel(); settings.BackColor = Palette.Surface;
            settings.Dock = DockStyle.Fill; settings.Margin = Padding.Empty; settings.Padding = new Padding(18, 14, 18, 12);
            settings.ColumnCount = 3; settings.RowCount = 3;
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label settingsTitle = LabelFor("监控偏好", 11, Palette.Text); settingsTitle.Font = new Font(uiFont, FontStyle.Bold);
            settings.Controls.Add(settingsTitle, 0, 0); settings.SetColumnSpan(settingsTitle, 3);
            settings.Controls.Add(LabelFor("自动刷新", 9, Palette.Muted), 0, 1);
            settings.Controls.Add(LabelFor("低电量阈值", 9, Palette.Muted), 1, 1);
            settings.Controls.Add(LabelFor("后台运行", 9, Palette.Muted), 2, 1);
            StyleCombo(rate, new object[] { "每 30 秒", "每 1 分钟", "每 2 分钟", "每 5 分钟" }); rate.AccessibleName = "自动刷新间隔";
            StyleCombo(limit, new object[] { "10%", "15%", "20%", "30%" });
            limit.AccessibleName = "低电量提醒阈值";
            settings.Controls.Add(rate, 0, 2); settings.Controls.Add(limit, 1, 2);
            FlowLayoutPanel checks = new FlowLayoutPanel(); checks.Dock = DockStyle.Fill; checks.FlowDirection = FlowDirection.TopDown;
            checks.WrapContents = false; checks.Margin = Padding.Empty;
            StyleCheck(notify, "低电量提醒"); StyleCheck(startup, "开机启动"); checks.Controls.Add(notify); checks.Controls.Add(startup); settings.Controls.Add(checks, 2, 2);
            layout.Controls.Add(settings, 0, 3);

            TableLayoutPanel footer = new TableLayoutPanel(); footer.Dock = DockStyle.Fill; footer.Margin = new Padding(0, 14, 0, 0);
            footer.ColumnCount = 4; footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            last.ForeColor = Palette.Muted; last.Font = new Font("Microsoft YaHei UI", 8); last.Dock = DockStyle.Fill; last.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(last, 0, 0);
            Button diagnostics = ButtonFor("连接排查", false); diagnostics.Dock = DockStyle.Fill; diagnostics.Click += delegate { ShowTroubleshooting(); }; footer.Controls.Add(diagnostics, 1, 0);
            Button hide = ButtonFor("收起到托盘", false); hide.Dock = DockStyle.Fill; hide.Click += delegate { Hide(); }; footer.Controls.Add(hide, 2, 0);
            Button quit = ButtonFor("退出", false); quit.Dock = DockStyle.Fill; quit.Click += delegate { host.Quit(); }; footer.Controls.Add(quit, 3, 0);
            layout.Controls.Add(footer, 0, 4);

            rate.SelectedIndexChanged += PreferencesChanged; limit.SelectedIndexChanged += PreferencesChanged;
            notify.CheckedChanged += PreferencesChanged; startup.CheckedChanged += PreferencesChanged;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            SyncSettings();
        }

        private static Label LabelFor(string text, float size, Color color)
        { return new Label { Text = text, ForeColor = color, Font = new Font("Microsoft YaHei UI", size), Dock = DockStyle.Top, Height = 26, AutoEllipsis = true }; }
        private static Button ButtonFor(string text, bool primary)
        {
            Button button = new Button(); button.Text = text; button.FlatStyle = FlatStyle.Flat;
            button.BackColor = primary ? Palette.Mint : Palette.Surface; button.ForeColor = primary ? Palette.Background : Palette.Text;
            button.FlatAppearance.BorderColor = primary ? Palette.Mint : Palette.Border; button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(137, 239, 207) : Color.FromArgb(37, 48, 65);
            button.Cursor = Cursors.Hand; button.Margin = new Padding(8, 0, 0, 0); button.AccessibleName = text; return button;
        }
        private static void StyleCombo(ComboBox combo, object[] entries)
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = Palette.Background; combo.ForeColor = Palette.Text; combo.Items.AddRange(entries);
            combo.Dock = DockStyle.Top; combo.Margin = new Padding(0, 3, 24, 0); combo.IntegralHeight = true;
        }
        private static void StyleCheck(CheckBox check, string text)
        { check.Text = text; check.AutoSize = true; check.ForeColor = Palette.Text; check.Margin = new Padding(0, 0, 0, 6); }
        private void PreferencesChanged(object sender, EventArgs e)
        {
            if (syncing || rate.SelectedIndex < 0 || limit.SelectedIndex < 0) return;
            host.Configure(new int[] { 30, 60, 120, 300 }[rate.SelectedIndex], new int[] { 10, 15, 20, 30 }[limit.SelectedIndex], notify.Checked, startup.Checked);
            SyncSettings();
        }
        public void SyncSettings()
        {
            syncing = true;
            try
            {
                rate.SelectedIndex = Array.IndexOf(new int[] { 30, 60, 120, 300 }, host.RefreshSeconds);
                limit.SelectedIndex = Array.IndexOf(new int[] { 10, 15, 20, 30 }, host.AlertThreshold);
                notify.Checked = host.NotificationsEnabled; startup.Checked = TrayContext.StartupEnabled();
            }
            finally { syncing = false; }
        }
        public void UpdateStatus(Reading[] values, DateTime updated, bool busy)
        {
            int mask = (values[0].Present ? 1 : 0) | (values[1].Present ? 2 : 0);
            if (mask != displayedMask)
            {
                deviceGrid.SuspendLayout();
                try
                {
                    deviceGrid.Controls.Clear();
                    for (int i = 0; i < 2; i++) cards[i].Visible = false;
                    if (mask == 0)
                    { deviceGrid.Controls.Add(emptyState, 0, 0); deviceGrid.SetColumnSpan(emptyState, 2); emptyState.Visible = true; }
                    else
                    {
                        int column = 0;
                        for (int i = 0; i < 2; i++) if (values[i].Present)
                        {
                            deviceGrid.Controls.Add(cards[i], column++, 0); cards[i].Margin = mask == 3 ? new Padding(i == 0 ? 0 : 8, 0, i == 0 ? 8 : 0, 0) : Padding.Empty;
                            deviceGrid.SetColumnSpan(cards[i], mask == 3 ? 1 : 2); cards[i].Visible = true;
                        }
                    }
                    displayedMask = mask;
                }
                finally { deviceGrid.ResumeLayout(true); }
            }
            for (int i = 0; i < 2; i++) cards[i].UpdateReading(values[i], host.AlertThreshold);
            refresh.Enabled = !busy; refresh.Text = busy ? "正在读取…" : "立即刷新";
            last.Text = busy ? "正在从接收器读取电量…" : updated == DateTime.MinValue ? "等待第一次读取" :
                "上次检测 " + updated.ToString("HH:mm:ss") + "\n关闭窗口后继续在托盘运行";
        }
        private void ShowTroubleshooting()
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "连接排查"; dialog.Font = uiFont; dialog.BackColor = Palette.Background; dialog.ForeColor = Palette.Text;
                dialog.AutoScaleMode = AutoScaleMode.Dpi; dialog.ClientSize = new Size(490, 294); dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent; dialog.MaximizeBox = false; dialog.MinimizeBox = false;
                Label body = LabelFor("暂时没有电量读数？\n\n1. 移动或点击鼠标，唤醒后再刷新。\n2. 关闭官方驱动和网页驱动，避免接口被占用。\n3. 重新插拔接收器，或只连接一款鼠标排查。\n\n仍无法读取时，导出设备诊断用于进一步适配。", 10, Palette.Text);
                body.Dock = DockStyle.Fill; body.Padding = new Padding(22); body.AutoEllipsis = false; dialog.Controls.Add(body);
                FlowLayoutPanel actions = new FlowLayoutPanel(); actions.Dock = DockStyle.Bottom; actions.Height = 58;
                actions.Padding = new Padding(14, 4, 14, 12); actions.FlowDirection = FlowDirection.RightToLeft;
                Button export = ButtonFor("导出诊断", true); export.Size = new Size(110, 36); export.Click += delegate { host.Diagnose(); dialog.Close(); };
                Button close = ButtonFor("返回", false); close.Size = new Size(90, 36); close.Click += delegate { dialog.Close(); };
                actions.Controls.Add(export); actions.Controls.Add(close); dialog.Controls.Add(actions); dialog.ShowDialog(this);
            }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) uiFont.Dispose(); base.Dispose(disposing); }
    }
}
