using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SdrBrightnessTray
{
    internal sealed class BrightnessPopup : Form
    {
        private const int BaseWidth = 320;
        private const int BaseHeight = 154;
        private const int BasePadding = 20;
        private const int TrackHeight = 6;
        private const int ThumbRadius = 9;

        private readonly DisplayController _controller = new DisplayController();
        private readonly Timer _applyTimer;
        private readonly Font _titleFont;
        private readonly Font _statusFont;
        private readonly Font _numberFont;
        private readonly Font _percentFont;
        private readonly Font _nitsFont;
        private readonly Font _hintFont;

        private int _value = 70;
        private bool _enabled;
        private bool _hasReading;
        private bool _dragging;
        private bool _hovering;
        private bool _focused;
        private string _status = "正在读取…";
        private string _error;
        private int _pendingPercent = -1;
        private int _lastHiddenTick;
        private Region _roundRegion;

        public event EventHandler ValueChanged;

        public BrightnessPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            TabStop = true;
            Text = "SDR 内容亮度";
            AccessibleName = "SDR 内容亮度";
            AccessibleDescription = "HDR 模式下 SDR 内容亮度";
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

            _titleFont = Theme.CreateFont(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" },
                10.5f, FontStyle.Bold);
            _statusFont = Theme.CreateFont(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" },
                8.5f, FontStyle.Regular);
            _numberFont = Theme.CreateFont(new string[] { "Segoe UI Semibold", "Segoe UI Variable Display", "Segoe UI" },
                28f, FontStyle.Regular);
            _percentFont = Theme.CreateFont(new string[] { "Segoe UI Semibold", "Segoe UI" },
                12f, FontStyle.Regular);
            _nitsFont = Theme.CreateFont(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" },
                9f, FontStyle.Regular);
            _hintFont = Theme.CreateFont(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" },
                8f, FontStyle.Regular);

            _applyTimer = new Timer();
            _applyTimer.Interval = 35;
            _applyTimer.Tick += ApplyTimerTick;

            ApplyScaledSize();
        }

        public int LastHiddenTick
        {
            get { return _lastHiddenTick; }
        }

        public string TooltipText
        {
            get
            {
                if (_error != null) return "SDR 内容亮度 · 调整失败";
                if (!_enabled) return "SDR 内容亮度 · " + _status;
                return string.Format("SDR 内容亮度 {0}% · {1} 尼特", _value,
                    DisplayController.NitsFromPercent(_value));
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindowStyle(Handle);
            ApplyRoundRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Surface);

            RectangleF bounds = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
            using (LinearGradientBrush background = new LinearGradientBrush(bounds,
                Theme.SurfaceTop, Theme.SurfaceBottom, 90f))
            {
                g.FillRectangle(background, bounds);
            }

            using (GraphicsPath border = Theme.RoundedRect(new RectangleF(0.5f, 0.5f,
                ClientSize.Width - 1f, ClientSize.Height - 1f), S(8)))
            using (Pen pen = new Pen(Theme.Border, 1f))
            {
                g.DrawPath(pen, border);
            }

            int pad = S(BasePadding);
            int width = ClientSize.Width;

            Rectangle titleRect = new Rectangle(pad, S(14), S(180), S(22));
            TextRenderer.DrawText(g, "SDR 内容亮度", _titleFont, titleRect, Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            DrawStatus(g, width - pad);

            int numberTop = S(35);
            string number = _value.ToString();
            Size numberSize = TextRenderer.MeasureText(g, number, _numberFont, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);
            Color numberColor = _enabled ? Theme.TextPrimary : Theme.TextDisabled;
            TextRenderer.DrawText(g, number, _numberFont, new Point(pad, numberTop), numberColor,
                TextFormatFlags.NoPadding);

            string percent = "%";
            Size percentSize = TextRenderer.MeasureText(g, percent, _percentFont,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            int percentTop = numberTop + numberSize.Height - percentSize.Height - S(1);
            TextRenderer.DrawText(g, percent, _percentFont,
                new Point(pad + numberSize.Width - S(1), percentTop),
                _enabled ? Theme.TextSecondary : Theme.TextDisabled, TextFormatFlags.NoPadding);

            if (_enabled)
            {
                string nits = string.Format("{0} 尼特", DisplayController.NitsFromPercent(_value));
                Size nitsSize = TextRenderer.MeasureText(g, nits, _nitsFont, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, nits, _nitsFont,
                    new Point(width - pad - nitsSize.Width, S(62)), Theme.TextSecondary,
                    TextFormatFlags.NoPadding);
            }

            RectangleF track = new RectangleF(pad, S(101), width - pad * 2, S(TrackHeight));
            DrawSlider(g, track);

            string footer;
            Color footerColor;
            if (_error != null)
            {
                footer = _error;
                footerColor = Theme.Error;
            }
            else if (!_enabled)
            {
                footer = _hasReading ? "打开 HDR 后即可调整" : "HDR 未开启或当前显示器不支持 HDR";
                footerColor = Theme.TextMuted;
            }
            else
            {
                footer = "拖动即时生效";
                footerColor = Theme.TextMuted;
            }
            TextRenderer.DrawText(g, footer, _hintFont, new Rectangle(pad, S(127), width - pad * 2, S(18)),
                footerColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawStatus(Graphics g, int right)
        {
            string text = _status;
            Size size = TextRenderer.MeasureText(g, text, _statusFont, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);
            int y = S(17) + (S(19) - size.Height) / 2;
            int textX = right - size.Width;
            Color dotColor = _enabled ? Theme.Accent : Theme.TextMuted;
            using (SolidBrush dot = new SolidBrush(dotColor))
            {
                g.FillEllipse(dot, textX - S(11), y + size.Height / 2 - S(2), S(5), S(5));
            }
            TextRenderer.DrawText(g, text, _statusFont, new Point(textX, y), Theme.TextSecondary,
                TextFormatFlags.NoPadding);
        }

        private void DrawSlider(Graphics g, RectangleF track)
        {
            float trackRadius = track.Height / 2f;
            using (GraphicsPath background = Theme.RoundedRect(track, trackRadius))
            using (SolidBrush brush = new SolidBrush(_enabled ? Theme.Track : Theme.TrackDisabled))
            {
                g.FillPath(brush, background);
            }

            float thumbX = track.X + track.Width * (_value / 100f);
            float centerY = track.Y + track.Height / 2f;

            if (_enabled && _value > 0)
            {
                RectangleF fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, thumbX - track.X), track.Height);
                using (GraphicsPath path = Theme.RoundedRect(fill, trackRadius))
                using (SolidBrush brush = new SolidBrush(Theme.Accent))
                {
                    g.FillPath(brush, path);
                }
            }

            float radius = S(ThumbRadius) + (_dragging ? S(1) : 0);
            Color thumbColor = _enabled ? Color.White : Theme.ThumbDisabled;
            using (SolidBrush thumb = new SolidBrush(thumbColor))
            {
                g.FillEllipse(thumb, thumbX - radius, centerY - radius, radius * 2, radius * 2);
            }
            if (_focused && _enabled)
            {
                using (Pen focus = new Pen(Color.FromArgb(150, Theme.Accent), Math.Max(1f, S(1))))
                {
                    g.DrawEllipse(focus, thumbX - radius - S(3), centerY - radius - S(3),
                        (radius + S(3)) * 2, (radius + S(3)) * 2);
                }
            }
        }

        public void ShowNearTray()
        {
            RefreshFromSystem();
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - S(12), area.Bottom - Height - S(12));
            if (Location.X < area.Left) Location = new Point(area.Left, Location.Y);
            if (Location.Y < area.Top) Location = new Point(Location.X, area.Top);
            Show();
            BringToFront();
            Activate();
            Focus();
        }

        public void RefreshFromSystem()
        {
            _applyTimer.Stop();
            _pendingPercent = -1;
            _dragging = false;
            _error = null;

            DisplaySnapshot snapshot = _controller.Refresh();
            if (!snapshot.HasHdrDisplay)
            {
                _enabled = false;
                _hasReading = snapshot.HasReading;
                _status = "未检测到 HDR";
                if (snapshot.HasReading) _value = DisplayController.PercentFromWhiteLevel(snapshot.WhiteLevel);
            }
            else if (!snapshot.HdrEnabled)
            {
                _enabled = false;
                _hasReading = snapshot.HasReading;
                _status = "HDR 未开启";
                if (snapshot.HasReading) _value = DisplayController.PercentFromWhiteLevel(snapshot.WhiteLevel);
            }
            else
            {
                _enabled = true;
                _hasReading = snapshot.HasReading;
                _status = "HDR 已开启";
                if (snapshot.HasReading) _value = DisplayController.PercentFromWhiteLevel(snapshot.WhiteLevel);
                if (snapshot.Error != null) _error = snapshot.Error;
            }
            Invalidate();
            OnValueChanged();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible) HidePopup();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HidePopup();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplyScaledSize();
            ApplyRoundRegion();
            if (Visible) ShowNearTray();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                HidePopup();
                e.Handled = true;
                return;
            }
            if (!_enabled) return;

            int next = _value;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) next--;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) next++;
            else if (e.KeyCode == Keys.PageDown) next -= 10;
            else if (e.KeyCode == Keys.PageUp) next += 10;
            else if (e.KeyCode == Keys.Home) next = 0;
            else if (e.KeyCode == Keys.End) next = 100;
            else return;

            SetValue(next, true, true);
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (!_enabled || e.Button != MouseButtons.Left) return;
            if (!HitTestSlider(e.Location)) return;
            _dragging = true;
            Capture = true;
            SetValueFromX(e.X, true, false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = _enabled && HitTestSlider(e.Location);
            if (hover != _hovering)
            {
                _hovering = hover;
                Cursor = hover ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            if (_dragging) SetValueFromX(e.X, true, false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;
            Capture = false;
            SetValueFromX(e.X, true, true);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_dragging && Capture == false)
            {
                _dragging = false;
                FlushApply();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hovering)
            {
                _hovering = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_enabled) return;
            SetValue(_value + (e.Delta > 0 ? 1 : -1), true, true);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _focused = true;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _focused = false;
            Invalidate();
        }

        private void SetValueFromX(int x, bool apply, bool immediate)
        {
            RectangleF track = new RectangleF(S(BasePadding), S(101),
                ClientSize.Width - S(BasePadding) * 2, S(TrackHeight));
            float ratio = (x - track.X) / track.Width;
            int value = (int)Math.Round(ratio * 100f);
            SetValue(value, apply, immediate);
        }

        private void SetValue(int value, bool apply, bool immediate)
        {
            value = Math.Max(0, Math.Min(100, value));
            bool changed = value != _value;
            _value = value;
            if (changed)
            {
                Invalidate();
                OnValueChanged();
            }
            if (!apply || !_enabled) return;

            _pendingPercent = value;
            if (immediate)
            {
                _applyTimer.Stop();
                FlushApply();
            }
            else if (!_applyTimer.Enabled)
            {
                _applyTimer.Start();
            }
        }

        private void ApplyTimerTick(object sender, EventArgs e)
        {
            _applyTimer.Stop();
            FlushApply();
        }

        private void FlushApply()
        {
            if (_pendingPercent < 0 || !_enabled) return;
            int percent = _pendingPercent;
            _pendingPercent = -1;
            ApplyResult result = _controller.Apply(percent);
            _error = result.Success ? null : result.Error;
            Invalidate();
            OnValueChanged();
        }

        private bool HitTestSlider(Point point)
        {
            RectangleF track = new RectangleF(S(BasePadding), S(101),
                ClientSize.Width - S(BasePadding) * 2, S(TrackHeight));
            float top = track.Y - S(15);
            float bottom = track.Bottom + S(15);
            return point.X >= track.X - S(12) && point.X <= track.Right + S(12) &&
                   point.Y >= top && point.Y <= bottom;
        }

        private void OnValueChanged()
        {
            EventHandler handler = ValueChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void HidePopup()
        {
            _lastHiddenTick = Environment.TickCount;
            _applyTimer.Stop();
            FlushApply();
            Hide();
        }

        private void ApplyScaledSize()
        {
            ClientSize = new Size(S(BaseWidth), S(BaseHeight));
        }

        private int S(float value)
        {
            return (int)Math.Round(value * DeviceDpi / 96f);
        }

        private void ApplyRoundRegion()
        {
            using (GraphicsPath path = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), S(8)))
            {
                Region old = _roundRegion;
                _roundRegion = new Region(path);
                Region = _roundRegion;
                if (old != null) old.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _applyTimer.Dispose();
                _titleFont.Dispose();
                _statusFont.Dispose();
                _numberFont.Dispose();
                _percentFont.Dispose();
                _nitsFont.Dispose();
                _hintFont.Dispose();
                if (_roundRegion != null) _roundRegion.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
