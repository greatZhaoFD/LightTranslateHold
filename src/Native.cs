using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LightTranslateHold
{
    internal static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;

        public const int SW_SHOWNOACTIVATE = 4;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const uint LLKHF_INJECTED = 0x00000010;
        public const uint LLKHF_LOWER_IL_INJECTED = 0x00000002;

        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int CS_DROPSHADOW = 0x00020000;

        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
            public Rectangle ToRectangle()
            {
                return new Rectangle(Left, Top, Math.Max(0, Right - Left), Math.Max(0, Bottom - Top));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public uint cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("psapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        public static INPUT Key(ushort vk, bool up)
        {
            INPUT input = new INPUT();
            input.type = 1;
            input.U.ki.wVk = vk;
            input.U.ki.dwFlags = up ? 2u : 0u;
            return input;
        }

        public static bool SendCopy()
        {
            INPUT[] keys = new INPUT[]
            {
                Key(0x11, false), // Ctrl down
                Key(0x43, false), // C down
                Key(0x43, true),  // C up
                Key(0x11, true)   // Ctrl up
            };
            return SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(INPUT))) == (uint)keys.Length;
        }

        public static int CurrentModifiers()
        {
            int mods = 0;
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= Hotkey.MOD_CONTROL;
            if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= Hotkey.MOD_ALT;
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= Hotkey.MOD_SHIFT;
            if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0)
                mods |= Hotkey.MOD_WIN;
            return mods;
        }

        public static bool ModifiersDown()
        {
            return CurrentModifiers() != 0;
        }

        public static int ModifierMaskForVk(int vk)
        {
            if (vk == 0x10 || vk == 0xA0 || vk == 0xA1) return Hotkey.MOD_SHIFT;
            if (vk == 0x11 || vk == 0xA2 || vk == 0xA3) return Hotkey.MOD_CONTROL;
            if (vk == 0x12 || vk == 0xA4 || vk == 0xA5) return Hotkey.MOD_ALT;
            if (vk == 0x5B || vk == 0x5C) return Hotkey.MOD_WIN;
            return 0;
        }

        public static void ReleaseModifiers(int mask)
        {
            INPUT[] inputs = new INPUT[8];
            int count = 0;
            if ((mask & Hotkey.MOD_ALT) != 0 && (GetAsyncKeyState(0x12) & 0x8000) != 0)
                inputs[count++] = Key(0x12, true);
            if ((mask & Hotkey.MOD_CONTROL) != 0 && (GetAsyncKeyState(0x11) & 0x8000) != 0)
                inputs[count++] = Key(0x11, true);
            if ((mask & Hotkey.MOD_SHIFT) != 0 && (GetAsyncKeyState(0x10) & 0x8000) != 0)
                inputs[count++] = Key(0x10, true);
            if ((mask & Hotkey.MOD_WIN) != 0)
            {
                if ((GetAsyncKeyState(0x5B) & 0x8000) != 0) inputs[count++] = Key(0x5B, true);
                if ((GetAsyncKeyState(0x5C) & 0x8000) != 0) inputs[count++] = Key(0x5C, true);
            }
            if (count > 0) SendInput((uint)count, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static Rectangle GetWorkArea(Point point)
        {
            return Screen.FromPoint(point).WorkingArea;
        }

        public static void TrimWorkingSet()
        {
            try { EmptyWorkingSet(GetCurrentProcess()); } catch { }
        }
    }
}
