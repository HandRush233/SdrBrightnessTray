using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SdrBrightnessTray
{
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly EventWaitHandle _showEvent;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly BrightnessPopup _popup;
        private readonly Control _dispatcher;
        private readonly Thread _listener;
        private readonly Icon _icon;
        private readonly Font _menuFont;
        private bool _exiting;

        public TrayContext(EventWaitHandle showEvent)
        {
            _showEvent = showEvent;

            _popup = new BrightnessPopup();
            _popup.ValueChanged += PopupValueChanged;
            _popup.RefreshFromSystem();

            _icon = LoadIcon();
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _icon;
            _notifyIcon.Text = "SDR 内容亮度";
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += NotifyIconMouseClick;

            _menuFont = Theme.CreateFont(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" },
                9.5f, FontStyle.Regular);
            _menu = new ContextMenuStrip();
            _menu.Font = _menuFont;
            _menu.ShowImageMargin = false;
            _menu.ShowCheckMargin = true;
            _menu.Padding = new Padding(4);
            _menu.Renderer = new DarkMenuRenderer();

            ToolStripMenuItem adjust = new ToolStripMenuItem("调整 SDR 亮度");
            adjust.Click += delegate { ShowPopup(); };
            _menu.Items.Add(adjust);

            _autoStartItem = new ToolStripMenuItem("开机自动启动");
            _autoStartItem.Click += delegate { ToggleAutostart(); };
            _menu.Items.Add(_autoStartItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitApplication(); };
            _menu.Items.Add(exit);

            _menu.Opening += MenuOpening;
            _notifyIcon.ContextMenuStrip = _menu;

            _dispatcher = new Control();
            _dispatcher.CreateControl();
            IntPtr unused = _dispatcher.Handle;

            _listener = new Thread(ListenForShowRequest);
            _listener.IsBackground = true;
            _listener.Name = "SdrBrightnessTray.ShowListener";
            _listener.Start();

            SystemEvents.SessionEnding += SessionEnding;
            UpdateTrayText();
        }

        private static Icon LoadIcon()
        {
            try
            {
                using (Stream stream = typeof(TrayContext).Assembly.GetManifestResourceStream("SdrBrightnessTray.AppIcon.ico"))
                {
                    if (stream != null) return new Icon(stream, SystemInformation.SmallIconSize);
                }
            }
            catch
            {
                // Fall through to the stock icon.
            }
            return (Icon)SystemIcons.Application.Clone();
        }

        private void NotifyIconMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ShowPopup();
        }

        private void ShowPopup()
        {
            if (_exiting) return;
            if (_popup.Visible)
            {
                _popup.Hide();
                return;
            }
            if (unchecked(Environment.TickCount - _popup.LastHiddenTick) < 220) return;
            _popup.ShowNearTray();
            UpdateTrayText();
        }

        private void PopupValueChanged(object sender, EventArgs e)
        {
            UpdateTrayText();
        }

        private void UpdateTrayText()
        {
            if (_notifyIcon == null) return;
            string text = _popup.TooltipText;
            if (text.Length > 63) text = text.Substring(0, 60) + "...";
            _notifyIcon.Text = text;
        }

        private void MenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _autoStartItem.Checked = Autostart.IsEnabled();
        }

        private void ToggleAutostart()
        {
            bool enable = !_autoStartItem.Checked;
            string error;
            if (!Autostart.SetEnabled(enable, out error))
            {
                MessageBox.Show("无法修改开机启动项。\r\n" + error, "SDR 内容亮度",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _autoStartItem.Checked = Autostart.IsEnabled();
        }

        private void ListenForShowRequest()
        {
            while (!_exiting)
            {
                try
                {
                    if (_showEvent.WaitOne(500))
                    {
                        if (_exiting) return;
                        _dispatcher.BeginInvoke(new MethodInvoker(ShowPopup));
                    }
                }
                catch
                {
                    return;
                }
            }
        }

        private void SessionEnding(object sender, SessionEndingEventArgs e)
        {
            if (_dispatcher.InvokeRequired)
            {
                try { _dispatcher.BeginInvoke(new MethodInvoker(ExitApplication)); }
                catch { }
            }
            else
            {
                ExitApplication();
            }
        }

        private void ExitApplication()
        {
            if (_exiting) return;
            _exiting = true;
            try { SystemEvents.SessionEnding -= SessionEnding; }
            catch { }

            try { _showEvent.Set(); }
            catch { }
            if (_listener != null)
            {
                try { _listener.Join(400); }
                catch { }
            }

            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch { }

            try { _popup.Dispose(); }
            catch { }
            try { _menu.Dispose(); }
            catch { }
            try { _dispatcher.Dispose(); }
            catch { }
            try { _icon.Dispose(); }
            catch { }
            try { _menuFont.Dispose(); }
            catch { }

            ExitThread();
        }

        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkMenuColorTable())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected) return;
                Rectangle bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using (GraphicsPath path = Theme.RoundedRect(bounds, 6))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(52, 52, 52)))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(brush, path);
                }
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle bounds = e.ImageRectangle;
                if (bounds.IsEmpty)
                {
                    bounds = new Rectangle(8, (e.Item.Height - 12) / 2, 12, 12);
                }
                using (Pen pen = new Pen(Theme.Accent, Math.Max(1.6f, e.Graphics.DpiX / 96f * 1.6f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(pen, new Point[]
                    {
                        new Point(bounds.Left + 1, bounds.Top + bounds.Height / 2),
                        new Point(bounds.Left + bounds.Width / 2 - 1, bounds.Bottom - 2),
                        new Point(bounds.Right - 1, bounds.Top + 2)
                    });
                }
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (Pen pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
                }
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Theme.TextSecondary;
                base.OnRenderArrow(e);
            }
        }

        private sealed class DarkMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(34, 34, 34); } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Color.FromArgb(52, 52, 52); } }
            public override Color MenuItemSelected { get { return Color.FromArgb(52, 52, 52); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(52, 52, 52); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(52, 52, 52); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(34, 34, 34); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(34, 34, 34); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(34, 34, 34); } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
            public override Color CheckBackground { get { return Color.FromArgb(34, 34, 34); } }
            public override Color CheckSelectedBackground { get { return Color.FromArgb(52, 52, 52); } }
            public override Color CheckPressedBackground { get { return Color.FromArgb(52, 52, 52); } }
        }
    }
}
