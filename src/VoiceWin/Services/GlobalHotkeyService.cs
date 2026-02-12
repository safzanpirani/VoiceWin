using System.Runtime.InteropServices;

namespace VoiceWin.Services;

public class GlobalHotkeyService : IDisposable
{
    private readonly nint _keyboardHookId;
    private readonly nint _mouseHookId;
    private readonly LowLevelKeyboardProc _keyboardHookProc;
    private readonly LowLevelMouseProc _mouseHookProc;
    private bool _isTargetDown;
    private DateTime _keyDownTime;
    
    public event EventHandler? HotkeyPressed;
    public event EventHandler? HotkeyReleased;
    
    public int TargetVirtualKey { get; set; } = 165;
    public int TargetModifiers { get; set; } = 0;
    public string Mode { get; set; } = "hold";
    
    private bool _toggleState;
    private bool _isRecording;
    private const int HybridHoldThresholdMs = 250;
    
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;

    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_MBUTTON = 0x04;
    private const int VK_XBUTTON1 = 0x05;
    private const int VK_XBUTTON2 = 0x06;
    private const ushort XBUTTON1 = 0x0001;
    private const ushort XBUTTON2 = 0x0002;

    private const int ModifierCtrl = 1;
    private const int ModifierAlt = 2;
    private const int ModifierShift = 4;
    private const int ModifierWin = 8;
    private const int ModifierMouseLeft = 16;
    private const int ModifierMouseRight = 32;
    private const int ModifierMouseMiddle = 64;
    private const int ModifierMouseX1 = 128;
    private const int ModifierMouseX2 = 256;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    public GlobalHotkeyService()
    {
        _keyboardHookProc = KeyboardHookCallback;
        _mouseHookProc = MouseHookCallback;
        _keyboardHookId = SetKeyboardHook(_keyboardHookProc);
        _mouseHookId = SetMouseHook(_mouseHookProc);
    }

    private nint SetKeyboardHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookExKeyboard(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private nint SetMouseHook(LowLevelMouseProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookExMouse(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        int vkCode = Marshal.ReadInt32(lParam);
        bool isTargetKey = vkCode == TargetVirtualKey;

        if (!isTargetKey)
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        bool isKeyDownEvent = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
        bool isKeyUpEvent = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

        if (!isKeyDownEvent && !isKeyUpEvent)
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        if (ShouldHandleTargetEvent(isKeyDownEvent, isKeyUpEvent))
        {
            HandleTargetEvent(isKeyDownEvent, isKeyUpEvent);
            return (nint)1;
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        if (!TryGetMouseEvent(wParam, lParam, out int vkCode, out bool isMouseDownEvent, out bool isMouseUpEvent))
        {
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        if (vkCode != TargetVirtualKey)
        {
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        if (ShouldHandleTargetEvent(isMouseDownEvent, isMouseUpEvent))
        {
            HandleTargetEvent(isMouseDownEvent, isMouseUpEvent);
            return (nint)1;
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private bool ShouldHandleTargetEvent(bool isDownEvent, bool isUpEvent)
    {
        if (isDownEvent)
        {
            return AreModifiersPressed();
        }

        if (isUpEvent)
        {
            return _isTargetDown;
        }

        return false;
    }

    private void HandleTargetEvent(bool isDownEvent, bool isUpEvent)
    {
        if (Mode == "hold")
        {
            if (isDownEvent && !_isTargetDown)
            {
                _isTargetDown = true;
                _keyDownTime = DateTime.UtcNow;
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }
            else if (isUpEvent && _isTargetDown)
            {
                _isTargetDown = false;
                HotkeyReleased?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (Mode == "toggle")
        {
            if (isDownEvent && !_isTargetDown)
            {
                _isTargetDown = true;
                _toggleState = !_toggleState;

                if (_toggleState)
                {
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    HotkeyReleased?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (isUpEvent)
            {
                _isTargetDown = false;
            }
        }
        else if (Mode == "hybrid")
        {
            if (isDownEvent && !_isTargetDown)
            {
                _isTargetDown = true;
                _keyDownTime = DateTime.UtcNow;

                if (!_isRecording)
                {
                    _isRecording = true;
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (isUpEvent && _isTargetDown)
            {
                _isTargetDown = false;
                double holdDuration = (DateTime.UtcNow - _keyDownTime).TotalMilliseconds;

                if (holdDuration >= HybridHoldThresholdMs)
                {
                    _isRecording = false;
                    HotkeyReleased?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    private static bool TryGetMouseEvent(nint wParam, nint lParam, out int vkCode, out bool isDownEvent, out bool isUpEvent)
    {
        vkCode = 0;
        isDownEvent = false;
        isUpEvent = false;

        if (wParam == WM_LBUTTONDOWN)
        {
            vkCode = VK_LBUTTON;
            isDownEvent = true;
            return true;
        }

        if (wParam == WM_LBUTTONUP)
        {
            vkCode = VK_LBUTTON;
            isUpEvent = true;
            return true;
        }

        if (wParam == WM_RBUTTONDOWN)
        {
            vkCode = VK_RBUTTON;
            isDownEvent = true;
            return true;
        }

        if (wParam == WM_RBUTTONUP)
        {
            vkCode = VK_RBUTTON;
            isUpEvent = true;
            return true;
        }

        if (wParam == WM_MBUTTONDOWN)
        {
            vkCode = VK_MBUTTON;
            isDownEvent = true;
            return true;
        }

        if (wParam == WM_MBUTTONUP)
        {
            vkCode = VK_MBUTTON;
            isUpEvent = true;
            return true;
        }

        if (wParam != WM_XBUTTONDOWN && wParam != WM_XBUTTONUP)
        {
            return false;
        }

        var hookData = Marshal.PtrToStructure<MsllHookStruct>(lParam);
        ushort buttonData = (ushort)((hookData.MouseData >> 16) & 0xFFFF);

        if (buttonData == XBUTTON1)
        {
            vkCode = VK_XBUTTON1;
        }
        else if (buttonData == XBUTTON2)
        {
            vkCode = VK_XBUTTON2;
        }
        else
        {
            return false;
        }

        isDownEvent = wParam == WM_XBUTTONDOWN;
        isUpEvent = wParam == WM_XBUTTONUP;
        return true;
    }

    public void Dispose()
    {
        if (_keyboardHookId != nint.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
        }

        if (_mouseHookId != nint.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true, EntryPoint = "SetWindowsHookEx")]
    private static extern nint SetWindowsHookExKeyboard(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true, EntryPoint = "SetWindowsHookEx")]
    private static extern nint SetWindowsHookExMouse(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private bool AreModifiersPressed()
    {
        if (TargetModifiers == 0) return true;
        
        bool ctrlRequired = (TargetModifiers & ModifierCtrl) != 0;
        bool altRequired = (TargetModifiers & ModifierAlt) != 0;
        bool shiftRequired = (TargetModifiers & ModifierShift) != 0;
        bool winRequired = (TargetModifiers & ModifierWin) != 0;
        bool leftMouseRequired = (TargetModifiers & ModifierMouseLeft) != 0;
        bool rightMouseRequired = (TargetModifiers & ModifierMouseRight) != 0;
        bool middleMouseRequired = (TargetModifiers & ModifierMouseMiddle) != 0;
        bool mouseX1Required = (TargetModifiers & ModifierMouseX1) != 0;
        bool mouseX2Required = (TargetModifiers & ModifierMouseX2) != 0;

        bool ctrlPressed = (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0;
        bool altPressed = (GetAsyncKeyState(VK_LMENU) & 0x8000) != 0 || (GetAsyncKeyState(VK_RMENU) & 0x8000) != 0;
        bool shiftPressed = (GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0;
        bool winPressed = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        bool leftMousePressed = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        bool rightMousePressed = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        bool middleMousePressed = (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
        bool mouseX1Pressed = (GetAsyncKeyState(VK_XBUTTON1) & 0x8000) != 0;
        bool mouseX2Pressed = (GetAsyncKeyState(VK_XBUTTON2) & 0x8000) != 0;

        return (!ctrlRequired || ctrlPressed) &&
               (!altRequired || altPressed) &&
               (!shiftRequired || shiftPressed) &&
               (!winRequired || winPressed) &&
               (!leftMouseRequired || leftMousePressed) &&
               (!rightMouseRequired || rightMousePressed) &&
               (!middleMouseRequired || middleMousePressed) &&
               (!mouseX1Required || mouseX1Pressed) &&
               (!mouseX2Required || mouseX2Pressed);
    }
}
