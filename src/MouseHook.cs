using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LightTranslateHold
{
    internal sealed class MouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public Native.POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private readonly Func<bool> _isVisible;
        private readonly Func<Point, bool> _isInside;
        private readonly Action _onOutsideClick;
        private readonly Native.LowLevelKeyboardProc _proc;
        private IntPtr _hook = IntPtr.Zero;

        public string LastError = "";

        public MouseHook(Func<bool> isVisible, Func<Point, bool> isInside, Action onOutsideClick)
        {
            _isVisible = isVisible;
            _isInside = isInside;
            _onOutsideClick = onOutsideClick;
            _proc = Callback;
        }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            _hook = Native.SetWindowsHookEx(WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                LastError = "鼠标钩子安装失败，错误码 " + Marshal.GetLastWin32Error();
                Log.Error(LastError);
                return false;
            }
            return true;
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                {
                    bool visible = false;
                    try { visible = _isVisible != null && _isVisible(); } catch { }
                    if (visible)
                    {
                        MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                            lParam, typeof(MSLLHOOKSTRUCT));
                        Point point = new Point(data.pt.X, data.pt.Y);
                        bool inside = false;
                        try { inside = _isInside != null && _isInside(point); } catch { }
                        if (!inside)
                        {
                            try { if (_onOutsideClick != null) _onOutsideClick(); } catch (Exception ex) { Log.Error(ex); }
                        }
                    }
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                try { Native.UnhookWindowsHookEx(_hook); } catch { }
                _hook = IntPtr.Zero;
            }
        }
    }
}
