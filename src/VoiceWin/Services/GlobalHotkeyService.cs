using System.Runtime.InteropServices;

namespace VoiceWin.Services;

public class GlobalHotkeyService : IDisposable
{
    private readonly nint _keyboardHookId;
    private readonly LowLevelKeyboardProc _keyboardHookProc;
    private readonly SynchronizationContext? _eventContext;
    private readonly Timer _mouseTargetPollTimer;

    private bool _isTargetDown;
    private DateTime _keyDownTime;
    private bool _isEnabled = true;
    private int _targetVirtualKey = 165;

    public event EventHandler? HotkeyPressed;
    public event EventHandler? HotkeyReleased;

    public int TargetVirtualKey
    {
        get => _targetVirtualKey;
        set
        {
            if (_targetVirtualKey == value)
            {
                return;
            }

            _targetVirtualKey = value;
            _isTargetDown = false;
            _toggleState = false;
            _isRecording = false;
        }
    }

    public int TargetModifiers { get; set; } = 0;
    public string Mode { get; set; } = "hold";
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;

            if (!value)
            {
                _isTargetDown = false;
                _toggleState = false;
                _isRecording = false;
            }
        }
    }

    private bool _toggleState;
    private bool _isRecording;
    private const int HybridHoldThresholdMs = 250;
    private const int MousePollIntervalMs = 8;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

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

    public GlobalHotkeyService()
    {
        _eventContext = SynchronizationContext.Current;
        _keyboardHookProc = KeyboardHookCallback;
        _keyboardHookId = SetKeyboardHook(_keyboardHookProc);
        _mouseTargetPollTimer = new Timer(OnMouseTargetPollTick, null, 0, MousePollIntervalMs);
    }

    private nint SetKeyboardHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookExKeyboard(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
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

    private void OnMouseTargetPollTick(object? _)
    {
        if (!IsEnabled || !IsMouseVirtualKey(TargetVirtualKey))
        {
            return;
        }

        bool isMouseTargetDown = (GetAsyncKeyState(TargetVirtualKey) & 0x8000) != 0;

        if (isMouseTargetDown && !_isTargetDown)
        {
            if (AreModifiersPressed())
            {
                HandleTargetEvent(isDownEvent: true, isUpEvent: false);
            }
        }
        else if (!isMouseTargetDown && _isTargetDown)
        {
            HandleTargetEvent(isDownEvent: false, isUpEvent: true);
        }
    }

    private static bool IsMouseVirtualKey(int virtualKey)
    {
        return virtualKey is VK_LBUTTON or VK_RBUTTON or VK_MBUTTON or VK_XBUTTON1 or VK_XBUTTON2;
    }

    private bool ShouldHandleTargetEvent(bool isDownEvent, bool isUpEvent)
    {
        if (!IsEnabled)
        {
            return false;
        }

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
                RaiseHotkeyPressed();
            }
            else if (isUpEvent && _isTargetDown)
            {
                _isTargetDown = false;
                RaiseHotkeyReleased();
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
                    RaiseHotkeyPressed();
                }
                else
                {
                    RaiseHotkeyReleased();
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
                    RaiseHotkeyPressed();
                }
            }
            else if (isUpEvent && _isTargetDown)
            {
                _isTargetDown = false;
                double holdDuration = (DateTime.UtcNow - _keyDownTime).TotalMilliseconds;

                if (holdDuration >= HybridHoldThresholdMs)
                {
                    _isRecording = false;
                    RaiseHotkeyReleased();
                }
            }
        }
    }

    private void RaiseHotkeyPressed()
    {
        RaiseHotkeyEvent(HotkeyPressed);
    }

    private void RaiseHotkeyReleased()
    {
        RaiseHotkeyEvent(HotkeyReleased);
    }

    private void RaiseHotkeyEvent(EventHandler? handler)
    {
        if (handler == null)
        {
            return;
        }

        if (_eventContext != null)
        {
            _eventContext.Post(_ => handler(this, EventArgs.Empty), null);
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => handler(this, EventArgs.Empty));
    }

    public void Dispose()
    {
        _mouseTargetPollTimer.Dispose();

        if (_keyboardHookId != nint.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true, EntryPoint = "SetWindowsHookEx")]
    private static extern nint SetWindowsHookExKeyboard(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

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
