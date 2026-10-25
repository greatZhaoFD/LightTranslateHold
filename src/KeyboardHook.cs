using System;
using System.Runtime.InteropServices;

namespace LightTranslateHold
{
    internal sealed class KeyboardHook : IDisposable
    {
        private readonly int _triggerVk;
        private readonly int _triggerMods;
        private readonly Action _onTriggerDown;
        private readonly Action _onTriggerUp;
        private readonly Action _onEscape;
        private readonly Func<bool> _isPopupVisible;
        private readonly bool _swallowTrigger;
        private readonly bool _ignoreInjected;
        private readonly Native.LowLevelKeyboardProc _proc;
        private IntPtr _hook = IntPtr.Zero;
        private bool _down;
        private bool _suppressUntilKeyUp;
        private bool _enabled = true;

        public string LastError = "";

        public KeyboardHook(int triggerVk, int triggerMods, Action onTriggerDown, Action onTriggerUp,
            Action onEscape, Func<bool> isPopupVisible, bool swallowTrigger, bool ignoreInjected)
        {
            _triggerVk = triggerVk;
            _triggerMods = triggerMods;
            _onTriggerDown = onTriggerDown;
            _onTriggerUp = onTriggerUp;
            _onEscape = onEscape;
            _isPopupVisible = isPopupVisible;
            _swallowTrigger = swallowTrigger;
            _ignoreInjected = ignoreInjected;
            _proc = Callback;
        }

        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                _enabled = value;
                if (!value) { _down = false; _suppressUntilKeyUp = false; }
            }
        }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            IntPtr module = Native.GetModuleHandle(null);
            _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, module, 0);
            if (_hook == IntPtr.Zero)
            {
                LastError = "键盘钩子安装失败，错误码 " + Marshal.GetLastWin32Error();
                Log.Error(LastError);
                return false;
            }
            Log.Info("keyboard hook installed, trigger=" + _triggerMods + "/" + _triggerVk);
            return true;
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                try { Native.UnhookWindowsHookEx(_hook); } catch { }
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
            if (!_enabled) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool isDown = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            bool isUp = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
            if (!isDown && !isUp) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

            Native.KBDLLHOOKSTRUCT data = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                lParam, typeof(Native.KBDLLHOOKSTRUCT));

            bool injected = (data.flags & Native.LLKHF_INJECTED) != 0
                || (data.flags & Native.LLKHF_LOWER_IL_INJECTED) != 0;
            if (_ignoreInjected && injected) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

            int vk = (int)data.vkCode;

            if (vk == _triggerVk)
            {
                if (isDown)
                {
                    int currentMods = Native.CurrentModifiers();
                    bool modsMatch = (currentMods & _triggerMods) == _triggerMods
                        && (currentMods & ~_triggerMods) == 0;
                    if (!_down && modsMatch)
                    {
                        _down = true;
                        _suppressUntilKeyUp = true;
                        Log.Info("hook trigger down");
                        try { if (_onTriggerDown != null) _onTriggerDown(); } catch (Exception ex) { Log.Error(ex); }
                        if (_swallowTrigger) return new IntPtr(1);
                    }
                    else if (_suppressUntilKeyUp && _swallowTrigger)
                    {
                        return new IntPtr(1);
                    }
                }
                else if (isUp)
                {
                    _suppressUntilKeyUp = false;
                    if (_down)
                    {
                        _down = false;
                        Log.Info("hook trigger up");
                        try { if (_onTriggerUp != null) _onTriggerUp(); } catch (Exception ex) { Log.Error(ex); }
                        if (_swallowTrigger) return new IntPtr(1);
                    }
                }
                return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            // Releasing Alt/Ctrl/Shift/Win while the trigger is held also ends the hold.
            if (isUp && _down)
            {
                int modifier = Native.ModifierMaskForVk(vk);
                if (modifier != 0 && (_triggerMods & modifier) != 0)
                {
                    _down = false;
                    Log.Info("hook trigger modifier up");
                    try { if (_onTriggerUp != null) _onTriggerUp(); } catch (Exception ex) { Log.Error(ex); }
                    return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
                }
            }

            if (vk == 0x1B) // Escape
            {
                bool visible = false;
                try { visible = _isPopupVisible != null && _isPopupVisible(); } catch { }
                if (visible)
                {
                    if (isDown && _onEscape != null)
                    {
                        try { _onEscape(); } catch (Exception ex) { Log.Error(ex); }
                    }
                    return new IntPtr(1);
                }
            }

            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }
}
