using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace LightTranslateHold
{
    internal sealed class SettingsForm : Form
    {
        public Settings Result;
        private readonly Settings _original;
        private readonly TextBox _endpoint = new TextBox();
        private readonly TextBox _model = new TextBox();
        private readonly TextBox _key = new TextBox();
        private readonly CheckBox _showKey = new CheckBox();
        private readonly ComboBox _preset = new ComboBox();
        private readonly TextBox _hotkeyBox = new TextBox();
        private readonly Button _changeHotkey = new Button();
        private readonly ComboBox _mode = new ComboBox();
        private readonly ComboBox _target = new ComboBox();
        private readonly CheckBox _autoStart = new CheckBox();
        private readonly Label _notice = new Label();
        private readonly Button _test = new Button();
        private readonly Button _save = new Button();
        private readonly Button _cancel = new Button();
        private bool _loading;
        private CancellationTokenSource _testCts;

        public SettingsForm(Settings current)
        {
            _original = current;
            Result = current.Clone();
            Text = "轻译 · 按住版设置";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Microsoft YaHei UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(700, 710);
            MinimumSize = new Size(640, 620);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18, 14, 18, 12);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);

            int row = 0;
            Action<Control> add = delegate(Control control)
            {
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(control, 0, row);
                row++;
            };

            add(MakeLabel("翻译服务（OpenAI 兼容接口）", true));

            FlowLayoutPanel presetRow = new FlowLayoutPanel();
            presetRow.AutoSize = true;
            presetRow.Dock = DockStyle.Top;
            presetRow.WrapContents = false;
            presetRow.Margin = new Padding(0, 4, 0, 4);
            presetRow.Controls.Add(MakeLabel("预设：", false));
            _preset.DropDownStyle = ComboBoxStyle.DropDownList;
            _preset.Width = 180;
            _preset.Items.AddRange(new object[] {
                "DeepSeek（默认）",
                "阿里云百炼（DashScope）",
                "OpenAI",
                "本地 Ollama",
                "自定义"
            });
            _preset.SelectedIndexChanged += PresetChanged;
            presetRow.Controls.Add(_preset);
            presetRow.Controls.Add(MakeLabel("  其他兼容服务选“自定义”，直接改下面三项。", false));
            add(presetRow);

            add(MakeLabel("接口地址 Base URL（可填 /v1 或完整地址）", false));
            _endpoint.Dock = DockStyle.Fill;
            _endpoint.Margin = new Padding(0, 2, 0, 8);
            add(_endpoint);

            add(MakeLabel("模型名", false));
            _model.Dock = DockStyle.Fill;
            _model.Margin = new Padding(0, 2, 0, 8);
            add(_model);

            add(MakeLabel("API Key（使用 Windows DPAPI 加密保存，不写入日志）", false));
            TableLayoutPanel keyRow = new TableLayoutPanel();
            keyRow.AutoSize = true;
            keyRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            keyRow.Dock = DockStyle.Top;
            keyRow.Margin = new Padding(0, 2, 0, 8);
            keyRow.ColumnCount = 2;
            keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _key.Dock = DockStyle.Fill;
            _key.UseSystemPasswordChar = true;
            keyRow.Controls.Add(_key, 0, 0);
            _showKey.Text = "显示";
            _showKey.AutoSize = true;
            _showKey.Margin = new Padding(8, 6, 0, 0);
            _showKey.CheckedChanged += delegate { _key.UseSystemPasswordChar = !_showKey.Checked; };
            keyRow.Controls.Add(_showKey, 1, 0);
            add(keyRow);

            add(MakeLabel("翻译方向", false));
            _target.DropDownStyle = ComboBoxStyle.DropDownList;
            _target.Dock = DockStyle.Fill;
            _target.Margin = new Padding(0, 2, 0, 8);
            _target.Items.AddRange(new object[] {
                "自动中英互译（中文 → 英文，英文 → 中文）",
                "一律译成中文",
                "一律译成英文"
            });
            add(_target);

            add(MakeLabel("触发键（默认 Alt+1；程序运行时会独占该组合）", false));
            FlowLayoutPanel hotkeyRow = new FlowLayoutPanel();
            hotkeyRow.AutoSize = true;
            hotkeyRow.Dock = DockStyle.Top;
            hotkeyRow.WrapContents = false;
            hotkeyRow.Margin = new Padding(0, 2, 0, 8);
            _hotkeyBox.ReadOnly = true;
            _hotkeyBox.Width = 240;
            _hotkeyBox.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            _hotkeyBox.TextAlign = HorizontalAlignment.Center;
            hotkeyRow.Controls.Add(_hotkeyBox);
            _changeHotkey.Text = "更改…";
            _changeHotkey.AutoSize = false;
            _changeHotkey.Size = new Size(84, 30);
            _changeHotkey.Margin = new Padding(8, 2, 0, 0);
            _changeHotkey.Click += ChangeHotkeyClick;
            hotkeyRow.Controls.Add(_changeHotkey);
            add(hotkeyRow);

            add(MakeLabel("触发方式", false));
            _mode.DropDownStyle = ComboBoxStyle.DropDownList;
            _mode.Dock = DockStyle.Fill;
            _mode.Margin = new Padding(0, 2, 0, 8);
            _mode.Items.AddRange(new object[] { "按住翻译（推荐）", "按一下翻译" });
            add(_mode);

            add(MakeLabel("浮窗关闭方式", false));
            Label closeHint = new Label();
            closeHint.Text = "按 Esc 或点击浮窗外部关闭；不操作会一直保留。";
            closeHint.AutoSize = true;
            closeHint.ForeColor = Color.FromArgb(100, 116, 139);
            closeHint.Margin = new Padding(0, 2, 0, 8);
            add(closeHint);

            _autoStart.Text = "登录 Windows 后自动启动";
            _autoStart.AutoSize = true;
            _autoStart.Margin = new Padding(0, 8, 0, 6);
            add(_autoStart);

            _notice.AutoSize = true;
            _notice.MaximumSize = new Size(640, 0);
            _notice.Margin = new Padding(0, 10, 0, 0);
            _notice.ForeColor = Color.FromArgb(100, 116, 139);
            _notice.Text = "配置文件：" + AppPaths.ConfigPath
                + "\r\n默认触发键 Alt+1，可点上方“更改…”设置；按住触发键才会翻译。";
            add(_notice);

            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(new Panel(), 0, row);
            row++;

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Height = 48;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(footer, 0, row);

            _test.Text = "测试连接（会调用一次 API）";
            _test.AutoSize = true;
            _test.Location = new Point(0, 8);
            _test.Click += TestClick;
            _save.Text = "保存";
            _save.Size = new Size(88, 32);
            _save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _save.Click += delegate { SaveSettings(); };
            _cancel.Text = "取消";
            _cancel.Size = new Size(88, 32);
            _cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(_test);
            footer.Controls.Add(_save);
            footer.Controls.Add(_cancel);
            footer.Resize += delegate
            {
                _save.Left = Math.Max(0, footer.Width - 196);
                _cancel.Left = Math.Max(0, footer.Width - 100);
            };
            Shown += delegate
            {
                _save.Left = Math.Max(0, footer.Width - 196);
                _cancel.Left = Math.Max(0, footer.Width - 100);
            };

            AcceptButton = _save;
            CancelButton = _cancel;

            _loading = true;
            _endpoint.Text = current.Endpoint;
            _model.Text = current.Model;
            _key.Text = current.ApiKey;
            _hotkeyBox.Text = current.HotkeyText;
            _mode.SelectedIndex = current.TriggerMode == "tap" ? 1 : 0;
            _target.SelectedIndex = current.TargetLanguage == "zh" ? 1 : current.TargetLanguage == "en" ? 2 : 0;
            _autoStart.Checked = current.AutoStart;
            _preset.SelectedIndex = DetectPreset(current.Endpoint);
            _loading = false;
        }

        private static Label MakeLabel(string text, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(0, bold ? 8 : 5, 0, 2);
            if (bold) l.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            return l;
        }

        private void ChangeHotkeyClick(object sender, EventArgs e)
        {
            using (HotkeyCaptureForm form = new HotkeyCaptureForm(_hotkeyBox.Text))
            {
                if (form.ShowDialog(this) == DialogResult.OK && !String.IsNullOrEmpty(form.Captured))
                    _hotkeyBox.Text = form.Captured;
            }
        }

        private static int DetectPreset(string endpoint)
        {
            string e = (endpoint ?? "").ToLowerInvariant();
            if (e.IndexOf("deepseek", StringComparison.Ordinal) >= 0) return 0;
            if (e.IndexOf("dashscope", StringComparison.Ordinal) >= 0
                || e.IndexOf("aliyuncs.com", StringComparison.Ordinal) >= 0) return 1;
            if (e.IndexOf("openai.com", StringComparison.Ordinal) >= 0) return 2;
            if (e.IndexOf("127.0.0.1:11434", StringComparison.Ordinal) >= 0) return 3;
            return 4;
        }

        private void PresetChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            if (_preset.SelectedIndex == 0)
            {
                _endpoint.Text = "https://api.deepseek.com";
                _model.Text = "deepseek-flash";
            }
            else if (_preset.SelectedIndex == 1)
            {
                _endpoint.Text = "https://dashscope.aliyuncs.com/compatible-mode/v1";
                _model.Text = "qwen-plus";
            }
            else if (_preset.SelectedIndex == 2)
            {
                _endpoint.Text = "https://api.openai.com/v1";
                _model.Text = "gpt-4.1-mini";
            }
            else if (_preset.SelectedIndex == 3)
            {
                _endpoint.Text = "http://127.0.0.1:11434/v1";
                _model.Text = "qwen2.5:7b";
            }
        }

        private Settings ReadForm()
        {
            Settings s = _original.Clone();
            s.Endpoint = _endpoint.Text.Trim();
            s.Model = _model.Text.Trim();
            s.ApiKey = _key.Text.Trim();
            s.Trigger = _hotkeyBox.Text.Trim();
            s.TriggerMode = _mode.SelectedIndex == 1 ? "tap" : "hold";
            s.TargetLanguage = _target.SelectedIndex == 1 ? "zh" : _target.SelectedIndex == 2 ? "en" : "auto";
            s.AutoStart = _autoStart.Checked;
            s.Normalize();
            return s;
        }

        private void SaveSettings()
        {
            try
            {
                Settings s = ReadForm();
                Translator.Address(s.Endpoint);
                if (String.IsNullOrWhiteSpace(s.Model)) throw new Exception("请填写模型名。");
                Keys hotkeyKey;
                int hotkeyMods;
                string hotkeyError;
                if (!Hotkey.TryParse(s.Trigger, out hotkeyKey, out hotkeyMods, out hotkeyError))
                    throw new Exception(hotkeyError);
                if (String.IsNullOrWhiteSpace(s.ApiKey) && !new Uri(s.Endpoint).IsLoopback)
                    throw new Exception("请填写 API Key；如使用本地免密接口，请把地址改为 127.0.0.1。");
                Result = s;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _notice.ForeColor = Color.FromArgb(185, 28, 28);
                _notice.Text = ex.Message;
            }
        }

        private async void TestClick(object sender, EventArgs e)
        {
            try
            {
                Settings s = ReadForm();
                Translator.Address(s.Endpoint);
                if (String.IsNullOrWhiteSpace(s.Model)) throw new Exception("请填写模型名。");
                if (String.IsNullOrWhiteSpace(s.ApiKey) && !new Uri(s.Endpoint).IsLoopback)
                    throw new Exception("请填写 API Key。");
                _test.Enabled = false;
                _test.Text = "测试中…";
                _notice.ForeColor = Color.FromArgb(100, 116, 139);
                _notice.Text = "正在请求接口，请稍候…";
                if (_testCts != null) { _testCts.Cancel(); _testCts.Dispose(); }
                _testCts = new CancellationTokenSource();
                using (Translator t = new Translator())
                {
                    TranslationDirection dir = TranslationDirection.Detect("Hello", s.TargetLanguage);
                    System.Text.StringBuilder result = new System.Text.StringBuilder();
                    await t.TranslateAsync(s, "Hello", dir, delegate(string piece) { result.Append(piece); }, _testCts.Token);
                    string text = result.ToString();
                    if (text.Length > 160) text = text.Substring(0, 160) + "…";
                    if (!IsDisposed && !Disposing)
                    {
                        _notice.ForeColor = Color.FromArgb(22, 101, 52);
                        _notice.Text = "连接成功：" + text;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    _notice.ForeColor = Color.FromArgb(185, 28, 28);
                    _notice.Text = "连接失败：" + ex.Message;
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    _test.Enabled = true;
                    _test.Text = "测试连接（会调用一次 API）";
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_testCts != null) { try { _testCts.Cancel(); } catch { } }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_testCts != null) { _testCts.Dispose(); _testCts = null; }
                if (Icon != null) Icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
