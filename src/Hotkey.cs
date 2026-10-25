using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LightTranslateHold
{
    internal static class Hotkey
    {
        public const int MOD_ALT = 1;
        public const int MOD_CONTROL = 2;
        public const int MOD_SHIFT = 4;
        public const int MOD_WIN = 8;

        public static bool TryParse(string text, out Keys key, out int modifiers, out string error)
        {
            key = Keys.None;
            modifiers = 0;
            error = "";
            if (String.IsNullOrWhiteSpace(text))
            {
                error = "请按下一个快捷键。";
                return false;
            }

            string[] parts = text.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = "快捷键格式不正确。";
                return false;
            }

            string keyToken = "";
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (token.Length == 0) continue;
                string lower = token.ToLowerInvariant();
                if (lower == "alt")
                {
                    modifiers |= MOD_ALT;
                }
                else if (lower == "ctrl" || lower == "control")
                {
                    modifiers |= MOD_CONTROL;
                }
                else if (lower == "shift")
                {
                    modifiers |= MOD_SHIFT;
                }
                else if (lower == "win" || lower == "windows")
                {
                    modifiers |= MOD_WIN;
                }
                else
                {
                    if (keyToken.Length != 0)
                    {
                        error = "快捷键只能包含一个非修饰键。";
                        return false;
                    }
                    keyToken = token;
                }
            }

            if (keyToken.Length == 0)
            {
                error = "快捷键缺少主键，例如 Alt+1。";
                return false;
            }
            if (!TryParseKey(keyToken, out key))
            {
                error = "不认识的按键：" + keyToken;
                return false;
            }
            if (IsModifierKey(key))
            {
                error = "主键不能是 Ctrl / Alt / Shift / Win。";
                return false;
            }
            if (modifiers == 0 && !IsAllowedSingleKey(key))
            {
                error = "单键快捷键请使用 F1–F12 或功能键；字母/数字请搭配 Alt、Ctrl 或 Shift。";
                return false;
            }
            return true;
        }

        public static string Format(Keys key, Keys modifierKeys)
        {
            return Format(key, ModifiersFromKeys(modifierKeys));
        }

        public static string Format(Keys key, int modifiers)
        {
            StringBuilder b = new StringBuilder();
            if ((modifiers & MOD_CONTROL) != 0) b.Append("Ctrl+");
            if ((modifiers & MOD_ALT) != 0) b.Append("Alt+");
            if ((modifiers & MOD_SHIFT) != 0) b.Append("Shift+");
            b.Append(KeyName(key));
            return b.ToString();
        }

        public static int ModifiersFromKeys(Keys keys)
        {
            int modifiers = 0;
            if ((keys & Keys.Control) != 0) modifiers |= MOD_CONTROL;
            if ((keys & Keys.Alt) != 0) modifiers |= MOD_ALT;
            if ((keys & Keys.Shift) != 0) modifiers |= MOD_SHIFT;
            return modifiers;
        }

        public static string NormalizeOrFallback(string text)
        {
            Keys key;
            int mods;
            string error;
            if (TryParse(text, out key, out mods, out error)) return Format(key, mods);
            return "Alt+1";
        }

        public static bool TryParseKey(string token, out Keys key)
        {
            key = Keys.None;
            if (String.IsNullOrEmpty(token)) return false;
            string t = token.Trim();
            if (t.Length == 1)
            {
                char c = Char.ToUpperInvariant(t[0]);
                if (c >= 'A' && c <= 'Z') { key = (Keys)c; return true; }
                if (c >= '0' && c <= '9') { key = (Keys)((int)Keys.D0 + (c - '0')); return true; }
            }
            if (t.Equals("Esc", StringComparison.OrdinalIgnoreCase)) { key = Keys.Escape; return true; }
            if (t.Equals("Space", StringComparison.OrdinalIgnoreCase)) { key = Keys.Space; return true; }
            if (t.Equals("Enter", StringComparison.OrdinalIgnoreCase)) { key = Keys.Enter; return true; }
            if (t.Equals("Tab", StringComparison.OrdinalIgnoreCase)) { key = Keys.Tab; return true; }
            if (t.Equals("Backspace", StringComparison.OrdinalIgnoreCase)) { key = Keys.Back; return true; }
            if (t.Equals("Delete", StringComparison.OrdinalIgnoreCase)) { key = Keys.Delete; return true; }
            if (t.Equals("Insert", StringComparison.OrdinalIgnoreCase)) { key = Keys.Insert; return true; }
            if (t.Equals("Home", StringComparison.OrdinalIgnoreCase)) { key = Keys.Home; return true; }
            if (t.Equals("End", StringComparison.OrdinalIgnoreCase)) { key = Keys.End; return true; }
            if (t.Equals("PageUp", StringComparison.OrdinalIgnoreCase)) { key = Keys.PageUp; return true; }
            if (t.Equals("PageDown", StringComparison.OrdinalIgnoreCase)) { key = Keys.PageDown; return true; }
            if (t.Equals("Up", StringComparison.OrdinalIgnoreCase)) { key = Keys.Up; return true; }
            if (t.Equals("Down", StringComparison.OrdinalIgnoreCase)) { key = Keys.Down; return true; }
            if (t.Equals("Left", StringComparison.OrdinalIgnoreCase)) { key = Keys.Left; return true; }
            if (t.Equals("Right", StringComparison.OrdinalIgnoreCase)) { key = Keys.Right; return true; }
            if (t.Equals("CapsLock", StringComparison.OrdinalIgnoreCase)) { key = Keys.CapsLock; return true; }
            if (t.Equals("Pause", StringComparison.OrdinalIgnoreCase)) { key = Keys.Pause; return true; }
            if (t.Equals("ScrollLock", StringComparison.OrdinalIgnoreCase)) { key = Keys.Scroll; return true; }
            if (t.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase)) { key = Keys.PrintScreen; return true; }
            if (Enum.TryParse<Keys>(t, true, out key)) return true;
            key = Keys.None;
            return false;
        }

        public static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
            if (key == Keys.Escape) return "Esc";
            if (key == Keys.Scroll) return "ScrollLock";
            if (key == Keys.Back) return "Backspace";
            return key.ToString();
        }

        public static bool IsModifierKey(Keys key)
        {
            if (key == Keys.Control || key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey) return true;
            if (key == Keys.Alt || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu) return true;
            if (key == Keys.Shift || key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey) return true;
            if (key == Keys.LWin || key == Keys.RWin) return true;
            return false;
        }

        public static bool IsAllowedSingleKey(Keys key)
        {
            if (key >= Keys.F1 && key <= Keys.F24) return true;
            if (key == Keys.CapsLock || key == Keys.Pause || key == Keys.Scroll
                || key == Keys.Insert || key == Keys.Home || key == Keys.End
                || key == Keys.PageUp || key == Keys.PageDown || key == Keys.PrintScreen) return true;
            return false;
        }

        public static int ModifierMask(Keys key)
        {
            if (key == Keys.Control || key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey) return MOD_CONTROL;
            if (key == Keys.Alt || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu) return MOD_ALT;
            if (key == Keys.Shift || key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey) return MOD_SHIFT;
            if (key == Keys.LWin || key == Keys.RWin) return MOD_WIN;
            return 0;
        }
    }

    internal sealed class HotkeyCaptureForm : Form
    {
        private readonly Label _label;
        private readonly TextBox _display;
        private string _captured = "";
        public string Captured { get { return _captured; } }

        public HotkeyCaptureForm(string current)
        {
            Text = "设置快捷键";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(430, 160);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            BackColor = Color.White;

            _label = new Label();
            _label.Text = "请按下新的快捷键（例如 Alt+1、F8）";
            _label.AutoSize = true;
            _label.Location = new Point(18, 18);
            Controls.Add(_label);

            _display = new TextBox();
            _display.ReadOnly = true;
            _display.Text = String.IsNullOrEmpty(current) ? "Alt+1" : current;
            _display.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            _display.Location = new Point(18, 50);
            _display.Width = 390;
            _display.TextAlign = HorizontalAlignment.Center;
            Controls.Add(_display);

            Label hint = new Label();
            hint.Text = "按 Esc 取消；字母/数字需搭配 Alt、Ctrl 或 Shift。";
            hint.AutoSize = true;
            hint.ForeColor = Color.FromArgb(100, 116, 139);
            hint.Location = new Point(18, 92);
            Controls.Add(hint);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(330, 120);
            cancel.Size = new Size(78, 28);
            Controls.Add(cancel);
            CancelButton = cancel;

            KeyDown += CaptureKeyDown;
            Shown += delegate { Focus(); };
        }

        private void CaptureKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            if (e.KeyCode == Keys.Back)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }
            if (Hotkey.IsModifierKey(e.KeyCode)) return;

            Keys key = e.KeyCode;
            Keys mods = e.Modifiers;
            string text = Hotkey.Format(key, mods);
            Keys parsedKey;
            int parsedMods;
            string error;
            if (!Hotkey.TryParse(text, out parsedKey, out parsedMods, out error))
            {
                _display.Text = error;
                return;
            }
            _captured = text;
            _display.Text = text;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
