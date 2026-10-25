using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace LightTranslateHold
{
    internal static class DebugOptions
    {
        public static string PopupScreenshotPath = "";
        public static bool Heartbeat = false;
        public static string FakeSelectionText = "";
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppPaths.Initialize(args);
            Log.Init();

            try
            {
                if (Array.IndexOf(args, "--self-test") >= 0)
                {
                    Environment.ExitCode = SelfTest.Run();
                    return;
                }
                if (Array.IndexOf(args, "--demo-popup") >= 0 || Array.IndexOf(args, "--demo-settings") >= 0)
                {
                    Environment.ExitCode = DemoMode.Run(args);
                    return;
                }

                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "--debug-popup-screenshot") DebugOptions.PopupScreenshotPath = args[i + 1];
                    if (args[i] == "--debug-fake-selection") DebugOptions.FakeSelectionText = args[i + 1];
                }
                DebugOptions.Heartbeat = Array.IndexOf(args, "--debug-heartbeat") >= 0;
                bool allowInjected = Array.IndexOf(args, "--allow-injected") >= 0;
                bool startInTray = Array.IndexOf(args, "--tray") >= 0;

                bool created;
                using (Mutex mutex = new Mutex(true, "Local\\LightTranslateHold.v1", out created))
                {
                    if (!created)
                    {
                        MessageBox.Show("轻译已经在后台运行。\r\n请查看右下角托盘图标。", "轻译",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                    {
                        Log.Error(e.Exception);
                    };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                    {
                        Log.Error(e.ExceptionObject as Exception ?? new Exception("Fatal runtime error"));
                    };
                    TaskScheduler.UnobservedTaskException += delegate(object sender, UnobservedTaskExceptionEventArgs e)
                    {
                        Log.Error(e.Exception);
                        e.SetObserved();
                    };

                    Settings settings;
                    try
                    {
                        settings = Settings.Load();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);
                        MessageBox.Show("配置文件读取失败：\r\n" + AppPaths.ConfigPath + "\r\n\r\n" + ex.Message,
                            "轻译", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Environment.ExitCode = 1;
                        return;
                    }

                    Log.Info("application start");
                    using (TrayContext context = new TrayContext(settings, allowInjected, startInTray))
                    {
                        Application.Run(context);
                    }
                    Log.Info("application exit");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                try
                {
                    MessageBox.Show("程序启动失败：\r\n" + ex.Message, "轻译",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
                Environment.ExitCode = 1;
            }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private Settings _settings;
        private readonly bool _allowInjected;
        private readonly bool _startInTray;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly Icon _icon;
        private readonly PopupForm _popup;
        private readonly Translator _translator;
        private KeyboardHook _hook;
        private MouseHook _mouseHook;
        private readonly WinTimer _holdTimer;
        private readonly WinTimer _uiTimer;
        private readonly WinTimer _startupTimer;
        private readonly WinTimer _trimTimer;
        private WinTimer _uiHeartbeatTimer;
        private readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        private readonly StringBuilder _full = new StringBuilder();
        private CancellationTokenSource _active;
        private int _generation;
        private bool _held;
        private bool _translating;
        private bool _settingsOpen;
        private bool _exiting;
        private bool _disposed;
        private IntPtr _foreground;
        private DateTime _startedAt;
        private string _lastTranslation = "";

        public TrayContext(Settings settings, bool allowInjected, bool startInTray)
        {
            _settings = settings.Clone();
            _allowInjected = allowInjected;
            _startInTray = startInTray;
            _settings.Normalize();

            try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
            catch { _icon = SystemIcons.Application; }

            _popup = new PopupForm(_settings);
            _translator = new Translator();

            _holdTimer = new WinTimer();
            _holdTimer.Interval = Math.Max(1, _settings.HoldThresholdMs);
            _holdTimer.Tick += HoldTimerTick;

            _uiTimer = new WinTimer();
            _uiTimer.Interval = 60;
            _uiTimer.Tick += delegate { DrainPendingToPopup(); };

            _startupTimer = new WinTimer();
            _startupTimer.Interval = 450;
            _startupTimer.Tick += StartupTimerTick;

            _trimTimer = new WinTimer();
            _trimTimer.Interval = 10000;
            _trimTimer.Tick += delegate
            {
                _trimTimer.Stop();
                if (!_popup.IsOpen && !_translating) Native.TrimWorkingSet();
            };

            _tray = new NotifyIcon();
            _tray.Icon = _icon;
            _tray.Visible = true;
            _tray.Text = TrayText();
            _menu = new ContextMenuStrip();
            _menu.Items.Add("翻译剪贴板", null, delegate { TranslateClipboard(); });
            _menu.Items.Add("复制上次译文", null, delegate { CopyLast(); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("设置…", null, delegate { OpenSettings(); });
            _menu.Items.Add("重新加载配置", null, delegate { ReloadSettings(); });
            _autoStartItem = new ToolStripMenuItem("开机自动启动");
            _autoStartItem.CheckOnClick = true;
            _autoStartItem.Checked = _settings.AutoStart;
            _autoStartItem.Click += AutoStartClick;
            _menu.Items.Add(_autoStartItem);
            _menu.Items.Add("打开数据目录", null, delegate { OpenDataDir(); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("退出", null, delegate { ExitApplication(); });
            _tray.ContextMenuStrip = _menu;
            _tray.DoubleClick += delegate { OpenSettings(); };

            if (DebugOptions.Heartbeat)
            {
                Thread heartbeat = new Thread(delegate()
                {
                    while (true) { Thread.Sleep(2000); Log.Info("heartbeat"); }
                });
                heartbeat.IsBackground = true;
                heartbeat.Start();
                _uiHeartbeatTimer = new WinTimer();
                _uiHeartbeatTimer.Interval = 2000;
                _uiHeartbeatTimer.Tick += delegate { Log.Info("ui heartbeat"); };
                _uiHeartbeatTimer.Start();
            }
            InstallHook();
            InstallMouseHook();
            _startupTimer.Start();
            _trimTimer.Start();
            Log.Info("tray started, trigger=" + _settings.HotkeyText + ", mode=" + _settings.TriggerMode);
        }

        private string TrayText()
        {
            string text = String.IsNullOrWhiteSpace(_settings.ApiKey)
                ? "轻译·按住版 · 未配置 API Key，右键设置"
                : "轻译·按住版 · 按 " + _settings.HotkeyText + " 翻译";
            if (text.Length > 62) text = text.Substring(0, 62);
            return text;
        }

        private void InstallHook()
        {
            if (_hook != null)
            {
                try { _hook.Dispose(); } catch { }
                _hook = null;
            }
            _hook = new KeyboardHook((int)_settings.TriggerKeyCode, _settings.TriggerModifiers,
                OnTriggerDown, OnTriggerUp, OnEscape,
                delegate { return _popup.IsOpen; }, true, !_allowInjected);
            if (!_hook.Install())
            {
                ShowBalloon("快捷键不可用", _hook.LastError, ToolTipIcon.Error);
            }
        }

        private void InstallMouseHook()
        {
            if (_mouseHook != null)
            {
                try { _mouseHook.Dispose(); } catch { }
                _mouseHook = null;
            }
            _mouseHook = new MouseHook(
                delegate { return _popup.IsOpen; },
                delegate(Point point) { return _popup.ContainsScreenPoint(point); },
                OnOutsideClick);
            if (!_mouseHook.Install())
            {
                Log.Error(_mouseHook.LastError);
            }
        }

        private void OnOutsideClick()
        {
            if (!_popup.IsOpen) return;
            CancelActive();
            _popup.HidePopup();
        }

        private void StartupTimerTick(object sender, EventArgs e)
        {
            _startupTimer.Stop();
            if (_startInTray)
            {
                if (String.IsNullOrWhiteSpace(_settings.ApiKey))
                    ShowBalloon("轻译已在后台运行", "还没有配置 API Key：右键托盘图标 → 设置，填写后保存。", ToolTipIcon.Info);
                return;
            }
            bool firstRun = !File.Exists(AppPaths.ConfigPath);
            if (firstRun)
            {
                OpenSettings();
                return;
            }
            if (String.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                ShowBalloon("轻译已在后台运行", "还没有配置 API Key：右键托盘图标 → 设置，填写后保存。", ToolTipIcon.Info);
            }
            else
            {
                ShowBalloon("轻译已在后台运行", "选中文字后按 " + _settings.HotkeyText + " 翻译；按 Esc 或点击浮窗外关闭。", ToolTipIcon.Info);
            }
        }

        private void HoldTimerTick(object sender, EventArgs e)
        {
            _holdTimer.Stop();
            if (!_held || _settingsOpen || _exiting) return;
            TriggerTranslation();
        }

        private void OnTriggerDown()
        {
            Log.Info("trigger down");
            if (_settingsOpen || _exiting) return;
            if (_translating || _popup.IsOpen)
            {
                CancelActive();
                _popup.HidePopup();
            }
            _held = true;
            _holdTimer.Stop();
            TriggerTranslation();
        }

        private void OnTriggerUp()
        {
            Log.Info("trigger up");
            _held = false;
            _holdTimer.Stop();
        }

        private void OnEscape()
        {
            if (!_popup.IsOpen) return;
            CancelActive();
            _popup.HidePopup();
        }

        private async void TriggerTranslation()
        {
            Log.Info("translation begin");
            if (_settingsOpen || _exiting) return;
            CancelActive();
            _generation++;
            int gen = _generation;
            _translating = true;
            _startedAt = DateTime.UtcNow;
            _foreground = Native.GetForegroundWindow();
            _full.Clear();
            DrainQueue();

            Rectangle initial = SelectionBounds.GetCaretOrMouse(_foreground);
            _popup.BeginLoading(initial, "识别语言…", false);
            _popup.SetStatus("读取选区…");

            Task<Rectangle?> boundsTask = SelectionBounds.TryGetAsync(_foreground, 500);
            string text;
            if (!String.IsNullOrEmpty(DebugOptions.FakeSelectionText))
            {
                await Task.Delay(150);
                text = DebugOptions.FakeSelectionText;
                Log.Info("using debug fake selection, chars=" + text.Length);
            }
            else
            {
                try
                {
                    text = await SelectionCapture.CaptureAsync(_foreground, _settings.RestoreClipboard, _settings.TriggerModifiers);
                }
                catch (Exception ex)
                {
                    if (gen != _generation) return;
                    Log.Info("capture failed: " + ex.Message);
                    _translating = false;
                    _popup.Fail(ex.Message);
                    return;
                }
            }
            if (gen != _generation) return;
            if (String.IsNullOrWhiteSpace(text))
            {
                Log.Info("selection capture empty");
                _translating = false;
                _popup.Fail("没有读到选中文字。请先选中可复制的文字，再按住 " + _settings.HotkeyText + "。");
                return;
            }
            if (text.Length > 20000)
            {
                _translating = false;
                _popup.Fail("选中文字过长（最多 20000 字符），请分段翻译。");
                return;
            }

            TranslationDirection direction = TranslationDirection.Detect(text, _settings.TargetLanguage);
            _popup.SetDirection(direction.Label);

            Rectangle? anchor = null;
            Task completed = await Task.WhenAny(boundsTask, Task.Delay(400));
            if (completed == boundsTask)
            {
                try { anchor = boundsTask.Result; } catch { }
            }
            if (!anchor.HasValue || anchor.Value.Width <= 0) anchor = SelectionBounds.GetCaretOrMouse(_foreground);
            _popup.UpdateAnchor(anchor);

            await RunApi(text, direction, gen);
        }

        private async void TranslateClipboard()
        {
            string text = "";
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                    text = Clipboard.GetText(TextDataFormat.UnicodeText).Trim();
            }
            catch { }
            if (String.IsNullOrWhiteSpace(text))
            {
                ShowBalloon("剪贴板没有文字", "先复制一段文字，再使用这个菜单。", ToolTipIcon.Info);
                return;
            }
            if (text.Length > 20000)
            {
                ShowBalloon("文字过长", "剪贴板文字超过 20000 字符，请分段翻译。", ToolTipIcon.Warning);
                return;
            }

            CancelActive();
            _popup.HidePopup();
            _generation++;
            int gen = _generation;
            _translating = true;
            _startedAt = DateTime.UtcNow;
            _foreground = Native.GetForegroundWindow();
            _full.Clear();
            DrainQueue();

            Point mouse = Cursor.Position;
            _popup.BeginLoading(new Rectangle(mouse.X, mouse.Y, 1, 18), "识别语言…", true);
            _popup.SetStatus("翻译剪贴板…");
            TranslationDirection direction = TranslationDirection.Detect(text, _settings.TargetLanguage);
            _popup.SetDirection(direction.Label);
            await RunApi(text, direction, gen);
        }

        private async Task RunApi(string text, TranslationDirection direction, int gen)
        {
            if (gen != _generation) return;
            Log.Info("API request begin, chars=" + text.Length + ", model=" + _settings.Model);
            Settings snapshot = _settings.Clone();
            CancellationTokenSource cts = new CancellationTokenSource();
            _active = cts;
            _popup.SetStatus("正在翻译…");
            _uiTimer.Start();
            try
            {
                await _translator.TranslateAsync(snapshot, text, direction, delegate(string piece)
                {
                    if (gen == _generation) _pending.Enqueue(piece);
                }, cts.Token);

                if (gen != _generation) return;
                DrainPendingToPopup();
                _lastTranslation = _full.ToString();
                double seconds = (DateTime.UtcNow - _startedAt).TotalSeconds;
                _popup.Complete("完成 · " + seconds.ToString("0.0") + " 秒");
                Log.Info("API request complete, chars=" + _lastTranslation.Length);
                SaveDebugPopup();
            }
            catch (OperationCanceledException)
            {
                if (gen == _generation)
                {
                    _translating = false;
                    _popup.Fail("已停止");
                }
            }
            catch (Exception ex)
            {
                if (gen == _generation)
                {
                    DrainPendingToPopup();
                    _translating = false;
                    Log.Error(ex);
                    _popup.Fail(ex.Message);
                    SaveDebugPopup();
                }
            }
            finally
            {
                if (gen == _generation)
                {
                    _translating = false;
                    _active = null;
                    _uiTimer.Stop();
                }
                try { cts.Dispose(); } catch { }
            }
        }

        private void DrainPendingToPopup()
        {
            string piece;
            while (_pending.TryDequeue(out piece))
            {
                _full.Append(piece);
                _popup.Append(piece);
            }
        }

        private void DrainQueue()
        {
            string ignored;
            while (_pending.TryDequeue(out ignored)) { }
        }

        private void CancelActive()
        {
            _generation++;
            if (_active != null)
            {
                try { _active.Cancel(); } catch { }
                _active = null;
            }
            _uiTimer.Stop();
            _translating = false;
            DrainQueue();
            _full.Clear();
        }

        private void OpenSettings()
        {
            if (_settingsOpen || _exiting) return;
            _settingsOpen = true;
            if (_hook != null) _hook.Enabled = false;
            CancelActive();
            _popup.HidePopup();
            try
            {
                using (SettingsForm form = new SettingsForm(_settings))
                {
                    if (form.ShowDialog() != DialogResult.OK) return;
                    Settings next = form.Result;
                    try { next.Save(); }
                    catch (Exception ex)
                    {
                        Log.Error(ex);
                        MessageBox.Show("设置保存失败：\r\n" + ex.Message, "轻译",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    _settings = next;
                    _settings.ApplyAutoStart();
                    _popup.UpdateSettings(_settings);
                    _autoStartItem.Checked = _settings.AutoStart;
                    _tray.Text = TrayText();
                    InstallHook();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                MessageBox.Show("设置窗口打开失败：\r\n" + ex.Message, "轻译",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _settingsOpen = false;
                if (_hook != null) _hook.Enabled = true;
            }
        }

        private void ReloadSettings()
        {
            try
            {
                Settings loaded = Settings.Load();
                _settings = loaded;
                _settings.ApplyAutoStart();
                _popup.UpdateSettings(_settings);
                _autoStartItem.Checked = _settings.AutoStart;
                _tray.Text = TrayText();
                InstallHook();
                ShowBalloon("配置已重新加载", "触发键：" + _settings.HotkeyText, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                ShowBalloon("配置加载失败", ex.Message, ToolTipIcon.Error);
            }
        }

        private void AutoStartClick(object sender, EventArgs e)
        {
            bool old = _settings.AutoStart;
            _settings.AutoStart = _autoStartItem.Checked;
            try
            {
                _settings.Save();
                _settings.ApplyAutoStart();
            }
            catch (Exception ex)
            {
                _settings.AutoStart = old;
                _autoStartItem.Checked = old;
                Log.Error(ex);
                ShowBalloon("设置失败", ex.Message, ToolTipIcon.Error);
            }
        }

        private void CopyLast()
        {
            if (String.IsNullOrEmpty(_lastTranslation))
            {
                ShowBalloon("还没有译文", "先翻译一次，再复制上次译文。", ToolTipIcon.Info);
                return;
            }
            try
            {
                Clipboard.SetText(_lastTranslation);
                ShowBalloon("已复制", "上次译文已复制到剪贴板。", ToolTipIcon.Info);
            }
            catch
            {
                ShowBalloon("复制失败", "剪贴板暂时被其他程序占用。", ToolTipIcon.Warning);
            }
        }

        private void OpenDataDir()
        {
            try { Process.Start("explorer.exe", AppPaths.DataDir); }
            catch (Exception ex) { ShowBalloon("无法打开目录", ex.Message, ToolTipIcon.Warning); }
        }

        private void SaveDebugPopup()
        {
            if (String.IsNullOrEmpty(DebugOptions.PopupScreenshotPath)) return;
            try
            {
                _popup.SaveScreenshot(DebugOptions.PopupScreenshotPath);
                Log.Info("debug popup screenshot saved: " + DebugOptions.PopupScreenshotPath);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try
            {
                if (_tray == null) return;
                if (text != null && text.Length > 250) text = text.Substring(0, 250) + "…";
                _tray.ShowBalloonTip(4000, title, text, icon);
            }
            catch { }
        }

        private void ExitApplication()
        {
            if (_exiting) return;
            _exiting = true;
            CancelActive();
            _startupTimer.Stop();
            _holdTimer.Stop();
            if (_hook != null) { try { _hook.Dispose(); } catch { } _hook = null; }
            if (_mouseHook != null) { try { _mouseHook.Dispose(); } catch { } _mouseHook = null; }
            if (_popup != null) { try { _popup.HidePopup(); } catch { } }
            if (_tray != null) { try { _tray.Visible = false; } catch { } }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                if (_hook != null) { try { _hook.Dispose(); } catch { } _hook = null; }
                if (_mouseHook != null) { try { _mouseHook.Dispose(); } catch { } _mouseHook = null; }
                if (_startupTimer != null) _startupTimer.Dispose();
                if (_trimTimer != null) _trimTimer.Dispose();
                if (_uiHeartbeatTimer != null) _uiHeartbeatTimer.Dispose();
                if (_holdTimer != null) _holdTimer.Dispose();
                if (_uiTimer != null) _uiTimer.Dispose();
                if (_popup != null) _popup.Dispose();
                if (_translator != null) _translator.Dispose();
                if (_menu != null) _menu.Dispose();
                if (_tray != null) { try { _tray.Visible = false; _tray.Dispose(); } catch { } }
                if (_icon != null && _icon != SystemIcons.Application) { try { _icon.Dispose(); } catch { } }
            }
            base.Dispose(disposing);
        }
    }
}
