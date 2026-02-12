using System.Runtime.InteropServices;

namespace VoiceWin.Services;

public class GlobalHotkeyService : IDisposable
{
    private readonly object _stateLock = new();
    private readonly nint _keyboardHookId;
    private readonly LowLevelKeyboardProc _keyboardHookProc;
    private readonly SynchronizationContext? _eventContext;

    private readonly AutoResetEvent _mouseHookReady = new(false);
    private Thread? _mouseHookThread;
    private uint _mouseHookThreadId;
    private nint _mouseHookId;
    private LowLevelMouseProc? _mouseHookProc;

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

            bool shouldRaiseRelease = false;
            lock (_stateLock)
            {
                shouldRaiseRelease = IsHotkeySessionActive();
                _targetVirtualKey = value;
                ResetHotkeyState();
            }

            if (shouldRaiseRelease)
            {
                RaiseHotkeyReleased();
            }

            UpdateMouseHookState();
        }
    }

    public int TargetModifiers { get; set; } = 0;
    public string Mode { get; set; } = "hold";
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            bool shouldRaiseRelease = false;

            lock (_stateLock)
            {
                _isEnabled = value;
                if (!value)
                {
                    shouldRaiseRelease = IsHotkeySessionActive();
                    ResetHotkeyState();
                }
            }

            if (shouldRaiseRelease)
            {
                RaiseHotkeyReleased();
            }

            UpdateMouseHookState();
        }
    }

    private bool _toggleState;
    private bool _isRecording;
    private const int HybridHoldThresholdMs = 250;

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const ushort XBUTTON1 = 0x0001;
    private const ushort XBUTTON2 = 0x0002;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Pt;
        public uint LPrivate;
    }

    public GlobalHotkeyService()
    {
        _eventContext = SynchronizationContext.Current;
        _keyboardHookProc = KeyboardHookCallback;
        _keyboardHookId = SetKeyboardHook(_keyboardHookProc);
        UpdateMouseHookState();
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

    private void UpdateMouseHookState()
    {
        if (IsEnabled && IsMouseVirtualKey(TargetVirtualKey))
        {
            StartMouseHookThread();
            return;
        }

        StopMouseHookThread();
    }

    private void StartMouseHookThread()
    {
        lock (_stateLock)
        {
            if (_mouseHookThread != null)
            {
                return;
            }

            _mouseHookReady.Reset();
            _mouseHookThread = new Thread(MouseHookThreadMain)
            {
                IsBackground = true,
                Name = "VoiceWin-MouseHook"
            };
            _mouseHookThread.Start();
        }

        _mouseHookReady.WaitOne(2000);
    }

    private void StopMouseHookThread()
    {
        Thread? threadToJoin = null;
        uint threadId;

        lock (_stateLock)
        {
            threadToJoin = _mouseHookThread;
            threadId = _mouseHookThreadId;
        }

        if (threadToJoin == null)
        {
            return;
        }

        if (threadId != 0)
        {
            PostThreadMessage(threadId, WM_QUIT, 0, 0);
        }

        threadToJoin.Join(2000);
    }

    private void MouseHookThreadMain()
    {
        try
        {
            var hookProc = new LowLevelMouseProc(MouseHookCallback);
            nint hookId = SetWindowsHookExMouse(WH_MOUSE_LL, hookProc, 0, 0);
            uint threadId = GetCurrentThreadId();

            lock (_stateLock)
            {
                _mouseHookProc = hookProc;
                _mouseHookId = hookId;
                _mouseHookThreadId = threadId;
            }

            _mouseHookReady.Set();

            if (hookId == nint.Zero)
            {
                return;
            }

            while (GetMessage(out Msg msg, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            lock (_stateLock)
            {
                if (_mouseHookId != nint.Zero)
                {
                    UnhookWindowsHookEx(_mouseHookId);
                }

                _mouseHookId = nint.Zero;
                _mouseHookThreadId = 0;
                _mouseHookProc = null;
                _mouseHookThread = null;
            }

            _mouseHookReady.Set();
        }
    }

    private static bool TryGetMouseEvent(nint wParam, nint lParam, out int vkCode, out bool isDownEvent, out bool isUpEvent)
    {
        vkCode = 0;
        isDownEvent = false;
        isUpEvent = false;

        if (wParam == WM_LBUTTONDOWN)
        {
            vkCode = HotkeyConstants.VK_LBUTTON;
            isDownEvent = true;
            return true;
        }

        if (wParam == WM_LBUTTONUP)
        {
            vkCode = HotkeyConstants.VK_LBUTTON;
            isUpEvent = true;
            return true;
        }

        if (wParam == WM_RBUTTONDOWN)
        {
            vkCode = HotkeyConstants.VK_RBUTTON;
            isDownEvent = true;
            return true;
        }

        if (wParam == WM_RBUTTONUP)
        {
            vkCode = HotkeyConstants.VK_RBUTTON;
            isUpEvent = true;
            return true;
        }

        if (wParam == WM_MBUTTONDOWN)
        {
            vkCode = HotkeyConstants.VK_MBUTTON;
            isDownEvent = true;
            return true;
        }

        if (wParam == WM_MBUTTONUP)
        {
            vkCode = HotkeyConstants.VK_MBUTTON;
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
            vkCode = HotkeyConstants.VK_XBUTTON1;
        }
        else if (buttonData == XBUTTON2)
        {
            vkCode = HotkeyConstants.VK_XBUTTON2;
        }
        else
        {
            return false;
        }

        isDownEvent = wParam == WM_XBUTTONDOWN;
        isUpEvent = wParam == WM_XBUTTONUP;
        return true;
    }

    private static bool IsMouseVirtualKey(int virtualKey)
    {
        return virtualKey is HotkeyConstants.VK_LBUTTON or HotkeyConstants.VK_RBUTTON or HotkeyConstants.VK_MBUTTON or HotkeyConstants.VK_XBUTTON1 or HotkeyConstants.VK_XBUTTON2;
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

    private bool IsHotkeySessionActive()
    {
        return _isTargetDown || _toggleState || _isRecording;
    }

    private void ResetHotkeyState()
    {
        _isTargetDown = false;
        _toggleState = false;
        _isRecording = false;
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
        StopMouseHookThread();
        _mouseHookReady.Dispose();

        if (_keyboardHookId != nint.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage([In] ref Msg lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private bool AreModifiersPressed()
    {
        if (TargetModifiers == 0) return true;

        bool ctrlRequired = (TargetModifiers & HotkeyConstants.ModifierCtrl) != 0;
        bool altRequired = (TargetModifiers & HotkeyConstants.ModifierAlt) != 0;
        bool shiftRequired = (TargetModifiers & HotkeyConstants.ModifierShift) != 0;
        bool winRequired = (TargetModifiers & HotkeyConstants.ModifierWin) != 0;
        bool leftMouseRequired = (TargetModifiers & HotkeyConstants.ModifierMouseLeft) != 0;
        bool rightMouseRequired = (TargetModifiers & HotkeyConstants.ModifierMouseRight) != 0;
        bool middleMouseRequired = (TargetModifiers & HotkeyConstants.ModifierMouseMiddle) != 0;
        bool mouseX1Required = (TargetModifiers & HotkeyConstants.ModifierMouseX1) != 0;
        bool mouseX2Required = (TargetModifiers & HotkeyConstants.ModifierMouseX2) != 0;

        bool ctrlPressed = (GetAsyncKeyState(HotkeyConstants.VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(HotkeyConstants.VK_RCONTROL) & 0x8000) != 0;
        bool altPressed = (GetAsyncKeyState(HotkeyConstants.VK_LMENU) & 0x8000) != 0 || (GetAsyncKeyState(HotkeyConstants.VK_RMENU) & 0x8000) != 0;
        bool shiftPressed = (GetAsyncKeyState(HotkeyConstants.VK_LSHIFT) & 0x8000) != 0 || (GetAsyncKeyState(HotkeyConstants.VK_RSHIFT) & 0x8000) != 0;
        bool winPressed = (GetAsyncKeyState(HotkeyConstants.VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(HotkeyConstants.VK_RWIN) & 0x8000) != 0;
        bool leftMousePressed = (GetAsyncKeyState(HotkeyConstants.VK_LBUTTON) & 0x8000) != 0;
        bool rightMousePressed = (GetAsyncKeyState(HotkeyConstants.VK_RBUTTON) & 0x8000) != 0;
        bool middleMousePressed = (GetAsyncKeyState(HotkeyConstants.VK_MBUTTON) & 0x8000) != 0;
        bool mouseX1Pressed = (GetAsyncKeyState(HotkeyConstants.VK_XBUTTON1) & 0x8000) != 0;
        bool mouseX2Pressed = (GetAsyncKeyState(HotkeyConstants.VK_XBUTTON2) & 0x8000) != 0;

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
