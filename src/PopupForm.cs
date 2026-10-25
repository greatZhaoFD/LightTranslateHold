using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LightTranslateHold
{
    internal sealed class PopupForm : Form
    {
        private Settings _settings;
        private Font _bodyFont;
        private Font _headerFont;
        private Font _footerFont;
        private float _scale = 1f;
        private bool _dark;

        private string _text = "";
        private string _displayText = "";
        private string _status = "";
        private string _direction = "";
        private string _error = "";
        private bool _dirty;
        private int _scroll;
        private int _contentHeight;
        private int _bodyHeight;
        private Rectangle _anchor;
        private bool _hasAnchor;

        private readonly Timer _renderTimer;
        private readonly Timer _copyTimer;
        private Rectangle _copyRect;
        private bool _copyHover;
        private bool _nativeVisible;
        private string _statusBeforeCopy = "";
        private int _lastWidth;
        private int _lastHeight;

        // Palette
        private Color _bg = Color.White;
        private Color _border = Color.FromArgb(216, 222, 233);
        private Color _textColor = Color.FromArgb(15, 23, 42);
        private Color _muted = Color.FromArgb(100, 116, 139);
        private Color _accent = Color.FromArgb(14, 165, 233);
        private Color _hover = Color.FromArgb(241, 245, 249);
        private Color _errorColor = Color.FromArgb(220, 38, 38);
        private Color _divider = Color.FromArgb(226, 232, 240);

        public PopupForm(Settings settings)
        {
            _settings = settings.Clone();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = _bg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();

            _renderTimer = new Timer();
            _renderTimer.Interval = 70;
            _renderTimer.Tick += delegate { if (_dirty) { LayoutPopup(); _dirty = false; } };
            _copyTimer = new Timer();
            _copyTimer.Interval = 1200;
            _copyTimer.Tick += delegate
            {
                _copyTimer.Stop();
                _status = _statusBeforeCopy;
                Invalidate();
            };

            ApplyTheme();
            BuildFonts();
            _status = "就绪";
            ClientSize = new Size(S(320), S(110));
        }

        public bool IsOpen { get { return _nativeVisible; } }
        public string CurrentText { get { return _text; } }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dpi = DeviceDpi;
                if (dpi > 0) _scale = Math.Max(1f, dpi / 96f);
            }
            catch { _scale = 1f; }
            try
            {
                int pref = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { }
            LayoutPopup();
        }

        public void UpdateSettings(Settings settings)
        {
            _settings = settings.Clone();
            ApplyTheme();
            BuildFonts();
            _dirty = true;
            if (_nativeVisible) LayoutPopup();
        }

        public void BeginLoading(Rectangle? anchor, string direction, bool manual)
        {
            CancelTimers();
            _text = "";
            _displayText = "";
            _error = "";
            _status = "读取选区…";
            _direction = direction ?? "";
            _scroll = 0;
            _contentHeight = 0;
            _bodyHeight = 0;
            SetAnchor(anchor);
            _dirty = true;
            ShowNoActivate();
            LayoutPopup();
        }

        public void UpdateAnchor(Rectangle? anchor)
        {
            SetAnchor(anchor);
            if (_nativeVisible) LayoutPopup();
        }

        public void SetDirection(string direction)
        {
            _direction = direction ?? "";
            Invalidate();
        }

        public void SetStatus(string status)
        {
            _status = status ?? "";
            Invalidate();
        }

        public void Append(string piece)
        {
            if (String.IsNullOrEmpty(piece)) return;
            _text += piece;
            _dirty = true;
            if (!_renderTimer.Enabled) _renderTimer.Start();
            Invalidate();
        }

        public void Complete(string status)
        {
            _status = status ?? "完成";
            _dirty = true;
            LayoutPopup();
            if (_renderTimer.Enabled) _renderTimer.Stop();
        }

        public void Fail(string message)
        {
            _error = message ?? "翻译失败";
            _status = "翻译失败";
            _dirty = true;
            LayoutPopup();
            if (_renderTimer.Enabled) _renderTimer.Stop();
        }

        public void HidePopup()
        {
            CancelTimers();
            _copyTimer.Stop();
            _renderTimer.Stop();
            _copyHover = false;
            _nativeVisible = false;
            try { Native.ShowWindow(Handle, 0); } catch { }
            try { Hide(); } catch { }
            Native.TrimWorkingSet();
        }

        private void SetAnchor(Rectangle? anchor)
        {
            if (anchor.HasValue && anchor.Value.Width > 0 && anchor.Value.Height > 0)
            {
                _anchor = anchor.Value;
                _hasAnchor = true;
            }
            else
            {
                _hasAnchor = false;
            }
        }

        public bool ContainsScreenPoint(Point screenPoint)
        {
            return _nativeVisible && Bounds.Contains(screenPoint);
        }

        private void CancelTimers()
        {
            _renderTimer.Stop();
            _copyTimer.Stop();
        }

        private void ShowNoActivate()
        {
            if (!IsHandleCreated)
            {
                IntPtr created = Handle;
            }
            _nativeVisible = true;
            LayoutPopup();
            try
            {
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Left, Top, Width, Height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
            catch { }
            if (!_renderTimer.Enabled) _renderTimer.Start();
        }

        private void ApplyTheme()
        {
            bool dark = false;
            if (_settings.Theme == "dark") dark = true;
            else if (_settings.Theme == "auto")
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        if (key != null)
                        {
                            object value = key.GetValue("AppsUseLightTheme");
                            if (value != null && Convert.ToInt32(value) == 0) dark = true;
                        }
                    }
                }
                catch { }
            }
            _dark = dark;
            if (dark)
            {
                _bg = Color.FromArgb(30, 41, 59);
                _border = Color.FromArgb(51, 65, 85);
                _textColor = Color.FromArgb(226, 232, 240);
                _muted = Color.FromArgb(148, 163, 184);
                _accent = Color.FromArgb(56, 189, 248);
                _hover = Color.FromArgb(51, 65, 85);
                _errorColor = Color.FromArgb(248, 113, 113);
                _divider = Color.FromArgb(51, 65, 85);
            }
            else
            {
                _bg = Color.White;
                _border = Color.FromArgb(216, 222, 233);
                _textColor = Color.FromArgb(15, 23, 42);
                _muted = Color.FromArgb(100, 116, 139);
                _accent = Color.FromArgb(14, 165, 233);
                _hover = Color.FromArgb(241, 245, 249);
                _errorColor = Color.FromArgb(220, 38, 38);
                _divider = Color.FromArgb(226, 232, 240);
            }
            BackColor = _bg;
        }

        private void BuildFonts()
        {
            Font oldBody = _bodyFont, oldHeader = _headerFont, oldFooter = _footerFont;
            string family = "Microsoft YaHei UI";
            try
            {
                _bodyFont = new Font(family, _settings.FontSize, FontStyle.Regular, GraphicsUnit.Point);
                _headerFont = new Font(family, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
                _footerFont = new Font(family, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                _bodyFont = new Font(FontFamily.GenericSansSerif, _settings.FontSize, FontStyle.Regular, GraphicsUnit.Point);
                _headerFont = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
                _footerFont = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            }
            if (oldBody != null) oldBody.Dispose();
            if (oldHeader != null) oldHeader.Dispose();
            if (oldFooter != null) oldFooter.Dispose();
        }

        private int S(int value) { return (int)Math.Round(value * _scale); }
        private float SF(float value) { return value * _scale; }

        private void LayoutPopup()
        {
            if (!IsHandleCreated) return;
            string clean = CleanForDisplay(_text);
            _displayText = clean;
            int padding = S(14);
            int headerH = S(27);
            int footerH = S(24);
            int gap = S(8);
            int maxWidth = S(_settings.MaxWidth);
            int minWidth = S(260);
            int maxBody = Math.Max(S(80), Math.Min(S(430),
                Screen.FromPoint(_hasAnchor ? new Point(_anchor.Left, _anchor.Top) : Cursor.Position).WorkingArea.Height - headerH - footerH - S(80)));

            string measureText = _displayText.Length > 0 ? _displayText : (_error.Length > 0 ? _error : "翻译中…");
            int desiredWidth = MeasurePreferredWidth(measureText) + padding * 2;
            if (desiredWidth < minWidth) desiredWidth = minWidth;
            if (desiredWidth > maxWidth) desiredWidth = maxWidth;
            int bodyWidth = Math.Max(S(80), desiredWidth - padding * 2);
            int textHeight = MeasureWrappedHeight(measureText, bodyWidth);
            if (textHeight > maxBody && desiredWidth < maxWidth)
            {
                desiredWidth = maxWidth;
                bodyWidth = Math.Max(S(80), desiredWidth - padding * 2);
                textHeight = MeasureWrappedHeight(measureText, bodyWidth);
            }
            if (textHeight < S(24)) textHeight = S(24);
            _contentHeight = textHeight;
            _bodyHeight = Math.Min(textHeight, maxBody);
            if (_scroll > Math.Max(0, _contentHeight - _bodyHeight)) _scroll = Math.Max(0, _contentHeight - _bodyHeight);
            int height = headerH + gap + _bodyHeight + gap + footerH + S(8);
            int width = desiredWidth;

            Rectangle work = Screen.FromPoint(_hasAnchor ? new Point(_anchor.Left, _anchor.Top) : Cursor.Position).WorkingArea;
            int x, y;
            if (_hasAnchor)
            {
                x = _anchor.Left;
                y = _anchor.Bottom + S(10);
                if (y + height > work.Bottom) y = _anchor.Top - height - S(10);
            }
            else
            {
                Point mouse = Cursor.Position;
                x = mouse.X + S(16);
                y = mouse.Y + S(24);
                if (x + width > work.Right) x = mouse.X - width - S(16);
                if (y + height > work.Bottom) y = mouse.Y - height - S(16);
            }
            x = Math.Max(work.Left, Math.Min(x, work.Right - width));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - height));

            bool changed = width != _lastWidth || height != _lastHeight || x != Left || y != Top;
            _lastWidth = width;
            _lastHeight = height;
            if (changed)
            {
                Bounds = new Rectangle(x, y, width, height);
                if (_nativeVisible)
                {
                    Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, width, height,
                        Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                }
            }
            Invalidate();
        }

        private int MeasurePreferredWidth(string text)
        {
            if (String.IsNullOrEmpty(text)) return S(220);
            int max = 0;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string line in lines)
            {
                Size size = TextRenderer.MeasureText(line.Length == 0 ? " " : line, _bodyFont,
                    new Size(Int32.MaxValue, Int32.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (size.Width > max) max = size.Width;
            }
            return Math.Max(S(160), max + S(6));
        }

        private int MeasureWrappedHeight(string text, int width)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            Size size = TextRenderer.MeasureText(text, _bodyFont, new Size(width, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            return size.Height;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int padding = S(14);
            int headerH = S(27);
            int gap = S(8);
            int footerH = S(24);
            int radius = S(10);
            Rectangle full = new Rectangle(0, 0, Width, Height);

            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), radius))
            {
                using (SolidBrush b = new SolidBrush(_bg)) g.FillPath(b, path);
                using (Pen p = new Pen(_border, 1f)) g.DrawPath(p, path);
            }

            // Header
            int dot = S(6);
            using (SolidBrush b = new SolidBrush(_accent))
                g.FillEllipse(b, padding, (headerH - dot) / 2, dot, dot);
            Rectangle dirRect = new Rectangle(padding + dot + S(7), 0, Width / 2, headerH);
            TextRenderer.DrawText(g, _direction, _headerFont, dirRect, _muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            Rectangle statusRect = new Rectangle(Width / 2, 0, Width / 2 - padding, headerH);
            TextRenderer.DrawText(g, _status, _headerFont, statusRect,
                _error.Length > 0 ? _errorColor : _muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            // Divider under header
            using (Pen p = new Pen(_divider, 1f))
                g.DrawLine(p, padding, headerH, Width - padding, headerH);

            // Body
            Rectangle bodyRect = new Rectangle(padding, headerH + gap, Width - padding * 2, _bodyHeight);
            Region oldClip = g.Clip;
            g.SetClip(bodyRect);
            if (_displayText.Length == 0)
            {
                string placeholder = _error.Length > 0 ? _error : "翻译中…";
                TextRenderer.DrawText(g, placeholder, _bodyFont, bodyRect, _error.Length > 0 ? _errorColor : _muted,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
            }
            else
            {
                Rectangle textRect = new Rectangle(bodyRect.Left, bodyRect.Top - _scroll, bodyRect.Width, _contentHeight + S(4));
                TextRenderer.DrawText(g, _displayText, _bodyFont, textRect, _textColor,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            }
            g.Clip = oldClip;

            // Scrollbar
            if (_contentHeight > _bodyHeight && _bodyHeight > 0)
            {
                int trackX = Width - S(7);
                int trackTop = bodyRect.Top;
                int trackHeight = bodyRect.Height;
                using (SolidBrush b = new SolidBrush(_divider))
                    g.FillRectangle(b, trackX, trackTop, S(3), trackHeight);
                int thumbHeight = Math.Max(S(20), (int)((long)trackHeight * _bodyHeight / Math.Max(1, _contentHeight)));
                int maxScroll = Math.Max(1, _contentHeight - _bodyHeight);
                int thumbY = trackTop + (int)((long)(trackHeight - thumbHeight) * _scroll / maxScroll);
                using (SolidBrush b = new SolidBrush(_muted))
                    g.FillRectangle(b, trackX, thumbY, S(3), thumbHeight);
            }

            // Footer
            int footerY = Height - footerH - S(8);
            using (Pen p = new Pen(_divider, 1f))
                g.DrawLine(p, padding, footerY - S(4), Width - padding, footerY - S(4));
            Rectangle hintRect = new Rectangle(padding, footerY, Width - padding * 2 - S(58), footerH);
            TextRenderer.DrawText(g, "Esc 关闭", _footerFont, hintRect, _muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            _copyRect = new Rectangle(Width - padding - S(48), footerY + S(2), S(48), footerH - S(4));
            using (GraphicsPath cp = RoundedRect(_copyRect, S(6)))
            {
                if (_copyHover)
                {
                    using (SolidBrush b = new SolidBrush(_hover)) g.FillPath(b, cp);
                }
                using (Pen p = new Pen(_border, 1f)) g.DrawPath(p, cp);
            }
            TextRenderer.DrawText(g, "复制", _footerFont, _copyRect, _text.Length > 0 ? _textColor : _muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            // A subtle accent strip on the left edge
            using (SolidBrush b = new SolidBrush(_accent))
                g.FillRectangle(b, 0, radius, S(2), Math.Max(0, Height - radius * 2));
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(Native.MA_NOACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = _copyRect.Contains(e.Location) && _text.Length > 0;
            if (hover != _copyHover)
            {
                _copyHover = hover;
                Cursor = hover ? Cursors.Hand : Cursors.Default;
                Invalidate(_copyRect);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_copyHover)
            {
                _copyHover = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && _copyRect.Contains(e.Location) && _text.Length > 0)
            {
                try
                {
                    Clipboard.SetText(_text);
                    _statusBeforeCopy = _status;
                    _status = "已复制";
                    _copyTimer.Stop();
                    _copyTimer.Start();
                    Invalidate();
                }
                catch
                {
                    _status = "剪贴板被占用";
                    Invalidate();
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_contentHeight <= _bodyHeight) return;
            int max = _contentHeight - _bodyHeight;
            _scroll -= (e.Delta / 120) * S(52);
            if (_scroll < 0) _scroll = 0;
            if (_scroll > max) _scroll = max;
            Invalidate();
        }

        public void SaveScreenshot(string path)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                if (!String.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                using (Bitmap bitmap = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private string CleanForDisplay(string raw)
        {
            if (String.IsNullOrEmpty(raw)) return "";
            string text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            text = Regex.Replace(text, @"```[^\n]*\n?", "");
            text = Regex.Replace(text, @"^\s{0,3}#{1,6}\s+", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "$1");
            text = Regex.Replace(text, @"__([^_]+)__", "$1");
            text = Regex.Replace(text, @"(?<![\w*])\*([^*\n]+)\*(?![\w*])", "$1");
            text = Regex.Replace(text, @"(?<![\w_])_([^_\n]+)_(?![\w_])", "$1");
            text = Regex.Replace(text, "`([^`]+)`", "$1");
            text = Regex.Replace(text, @"^\s*[-*+]\s+", "• ", RegexOptions.Multiline);
            return text.Trim();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelTimers();
                if (_renderTimer != null) _renderTimer.Dispose();
                if (_copyTimer != null) _copyTimer.Dispose();
                if (_bodyFont != null) _bodyFont.Dispose();
                if (_headerFont != null) _headerFont.Dispose();
                if (_footerFont != null) _footerFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
