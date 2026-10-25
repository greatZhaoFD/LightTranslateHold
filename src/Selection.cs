using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace LightTranslateHold
{
    internal static class SelectionCapture
    {
        private static bool _uiaTextDisabled;

        public static async Task<string> CaptureAsync(IntPtr foreground, bool restore, int triggerModifiers)
        {
            string viaUia = await TryUiaTextAsync(foreground, 700);
            if (!String.IsNullOrWhiteSpace(viaUia))
            {
                Log.Info("capture: UI Automation selected text, chars=" + viaUia.Length);
                return viaUia.Trim();
            }

            if (triggerModifiers != 0)
            {
                Native.ReleaseModifiers(triggerModifiers);
                await Task.Delay(40);
            }

            TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
            Thread worker = new Thread(delegate()
            {
                try
                {
                    string text = CaptureBlocking(foreground, restore);
                    completion.TrySetResult(text);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            worker.IsBackground = true;
            worker.Name = "LightTranslateHold.SelectionCapture";
            try { worker.SetApartmentState(ApartmentState.STA); } catch { }
            worker.Start();

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(5000));
            if (finished != completion.Task)
                throw new Exception("读取选区超时。剪贴板可能被其他程序占用，请重试。");
            return await completion.Task;
        }

        private static async Task<string> TryUiaTextAsync(IntPtr foreground, int timeoutMs)
        {
            if (_uiaTextDisabled) return null;
            Task<string> task = Task.Run<string>(delegate { return TryUiaText(foreground); });
            Task completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completed != task) { _uiaTextDisabled = true; Log.Info("UIA text timed out"); return null; }
            try { return task.Result; } catch { return null; }
        }

        private static string TryUiaText(IntPtr foreground)
        {
            try
            {
                if (Native.GetForegroundWindow() != foreground) return null;
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null) return null;
                if (focused.Current.IsPassword) return null;
                object patternObject;
                if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out patternObject)) return null;
                TextPattern pattern = patternObject as TextPattern;
                if (pattern == null) return null;
                TextPatternRange[] ranges = pattern.GetSelection();
                if (ranges == null || ranges.Length == 0) return null;
                string text = ranges[0].GetText(20000);
                return String.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception ex)
            {
                Log.Info("UIA selected text unavailable: " + ex.GetType().Name);
                return null;
            }
        }
        private static bool IsSafeClipboardFormat(string format)
        {
            if (String.IsNullOrEmpty(format)) return false;
            if (format == DataFormats.UnicodeText || format == DataFormats.Text
                || format == DataFormats.FileDrop || format == DataFormats.CommaSeparatedValue
                || format == "HTML Format" || format == "Rich Text Format"
                || format == "text/html" || format == "text/rtf")
                return true;
            return false;
        }
        private static string CaptureBlocking(IntPtr foreground, bool restore)
        {
            Log.Info("capture: start");
            for (int i = 0; i < 20 && Native.ModifiersDown(); i++) Thread.Sleep(25);
            if (Native.ModifiersDown())
                throw new Exception("请松开 Ctrl / Alt / Shift 后再按翻译键。");
            if (Native.GetForegroundWindow() != foreground)
                throw new Exception("窗口已切换，请重新选中文字再试。");

            IDataObject snapshot = null;
            bool snapshotReady = false;
            if (restore)
            {
                try
                {
                    Log.Info("capture: clipboard snapshot start");
                    IDataObject old = Clipboard.GetDataObject();
                    if (old != null)
                    {
                        DataObject copy = new DataObject();
                        foreach (string format in old.GetFormats(false))
                        {
                            if (!IsSafeClipboardFormat(format)) continue;
                            try
                            {
                                object value = old.GetData(format, false);
                                if (value != null) copy.SetData(format, false, value);
                            }
                            catch { }
                        }
                        if (copy.GetFormats(false).Length > 0) { snapshot = copy; snapshotReady = true; }
                    }
                    Log.Info("capture: clipboard snapshot done");
                }
                catch (Exception ex) { Log.Info("capture: clipboard snapshot failed " + ex.GetType().Name); }
            }

            uint copied = 0;
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    uint before = Native.GetClipboardSequenceNumber();
                    if (Native.GetForegroundWindow() != foreground)
                        throw new Exception("窗口已切换，请重新选中文字再试。");
                    Log.Info("capture: send copy attempt " + attempt);
                    if (!Native.SendCopy())
                        throw new Exception("无法向当前窗口发送复制命令。");

                    for (int i = 0; i < 24; i++)
                    {
                        Thread.Sleep(25);
                        if (i == 0 || i == 23) Log.Info("capture: poll " + i);
                        if (Native.GetClipboardSequenceNumber() == before) continue;
                        try
                        {
                            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                            {
                                string text = Clipboard.GetText(TextDataFormat.UnicodeText);
                                if (!String.IsNullOrWhiteSpace(text))
                                {
                                    Log.Info("capture: got text, chars=" + text.Length);
                                    copied = Native.GetClipboardSequenceNumber();
                                    return text.Trim();
                                }
                            }
                        }
                        catch (ExternalException) { }
                    }
                }
                throw new Exception("没有读到选中文字。请先选中可复制的文字，再按住翻译键。");
            }
            finally
            {
                if (restore && snapshotReady && copied != 0 && Native.GetClipboardSequenceNumber() == copied)
                {
                    try { Clipboard.SetDataObject(snapshot, true); }
                    catch { }
                }
            }
        }
    }

    internal static class SelectionBounds
    {
        private static bool _uiaDisabled;

        public static async Task<Rectangle?> TryGetAsync(IntPtr foreground, int timeoutMs)
        {
            if (_uiaDisabled) return null;
            Task<Rectangle?> task = Task.Run<Rectangle?>(delegate { return TryUia(foreground); });
            Task completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completed != task)
            {
                _uiaDisabled = true;
                Log.Info("UI Automation selection bounds timed out; falling back to caret/mouse.");
                return null;
            }
            try { return task.Result; }
            catch { return null; }
        }

        public static Rectangle GetCaretOrMouse(IntPtr foreground)
        {
            try
            {
                uint pid;
                uint tid = Native.GetWindowThreadProcessId(foreground, out pid);
                if (tid != 0)
                {
                    Native.GUITHREADINFO info = new Native.GUITHREADINFO();
                    info.cbSize = (uint)Marshal.SizeOf(typeof(Native.GUITHREADINFO));
                    if (Native.GetGUIThreadInfo(tid, ref info) && info.hwndCaret != IntPtr.Zero)
                    {
                        Native.POINT p = new Native.POINT(info.rcCaret.Left, info.rcCaret.Bottom);
                        if (Native.ClientToScreen(info.hwndCaret, ref p))
                        {
                            int h = Math.Max(16, info.rcCaret.Height);
                            return new Rectangle(p.X, p.Y, Math.Max(1, info.rcCaret.Width), h);
                        }
                    }
                }
            }
            catch { }

            Point mouse = Cursor.Position;
            return new Rectangle(mouse.X, mouse.Y, 1, 18);
        }

        private static Rectangle? TryUia(IntPtr foreground)
        {
            try
            {
                if (Native.GetForegroundWindow() != foreground) return null;
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null) return null;

                object patternObject;
                if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out patternObject)) return null;
                TextPattern pattern = patternObject as TextPattern;
                if (pattern == null) return null;

                TextPatternRange[] ranges = pattern.GetSelection();
                if (ranges == null || ranges.Length == 0) return null;

                System.Windows.Rect[] rects = ranges[0].GetBoundingRectangles();
                if (rects == null || rects.Length == 0) return null;

                int left = Int32.MaxValue, top = Int32.MaxValue, right = Int32.MinValue, bottom = Int32.MinValue;
                foreach (System.Windows.Rect rect in rects)
                {
                    double w = rect.Width, h = rect.Height;
                    if (w <= 0 || h <= 0) continue;
                    int x1 = (int)Math.Round(rect.X);
                    int y1 = (int)Math.Round(rect.Y);
                    int x2 = (int)Math.Round(rect.X + w);
                    int y2 = (int)Math.Round(rect.Y + h);
                    if (x2 <= 0 || y2 <= 0) continue;
                    left = Math.Min(left, x1);
                    top = Math.Min(top, y1);
                    right = Math.Max(right, x2);
                    bottom = Math.Max(bottom, y2);
                }
                if (left == Int32.MaxValue || right <= left || bottom <= top) return null;

                Rectangle result = Rectangle.FromLTRB(left, top, right, bottom);
                Rectangle virtualScreen = SystemInformation.VirtualScreen;
                if (!result.IntersectsWith(virtualScreen)) return null;
                return result;
            }
            catch (Exception ex)
            {
                Log.Info("UIA bounds unavailable: " + ex.GetType().Name);
                return null;
            }
        }
    }
}
