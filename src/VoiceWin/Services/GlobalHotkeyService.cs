using System.Runtime.InteropServices;

namespace VoiceWin.Services;

public class GlobalHotkeyService : IDisposable
{
    private readonly nint _hookId;
    private readonly LowLevelKeyboardProc _hookProc;
    private bool _isKeyDown;
    private DateTime _keyDownTime;
    
    public event EventHandler? HotkeyPressed;
    public event EventHandler? HotkeyReleased;
    
    public int TargetVirtualKey { get; set; } = 165;
    public int TargetModifiers { get; set; } = 0;
    public string Mode { get; set; } = "hold";
    
    private bool _toggleState;
    private bool _isRecording;
    private bool _recordingStartedByTap;
    private const int HybridHoldThresholdMs = 250;
    
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

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    public GlobalHotkeyService()
    {
        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);
    }

    private nint SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            if (vkCode == TargetVirtualKey && AreModifiersPressed())
            {
                bool isKeyDownEvent = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
                bool isKeyUpEvent = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

                if (Mode == "hold")
                {
                    if (isKeyDownEvent && !_isKeyDown)
                    {
                        _isKeyDown = true;
                        _keyDownTime = DateTime.UtcNow;
                        HotkeyPressed?.Invoke(this, EventArgs.Empty);
                    }
                    else if (isKeyUpEvent && _isKeyDown)
                    {
                        _isKeyDown = false;
                        HotkeyReleased?.Invoke(this, EventArgs.Empty);
                    }
                }
                else if (Mode == "toggle")
                {
                    if (isKeyDownEvent && !_isKeyDown)
                    {
                        _isKeyDown = true;
                        _toggleState = !_toggleState;
                        
                        if (_toggleState)
                            HotkeyPressed?.Invoke(this, EventArgs.Empty);
                        else
                            HotkeyReleased?.Invoke(this, EventArgs.Empty);
                    }
                    else if (isKeyUpEvent)
                    {
                        _isKeyDown = false;
                    }
                }
                else if (Mode == "hybrid")
                {
                    if (isKeyDownEvent && !_isKeyDown)
                    {
                        _isKeyDown = true;
                        _keyDownTime = DateTime.UtcNow;

                        if (!_isRecording)
                        {
                            _isRecording = true;
                            _recordingStartedByTap = false;
                            HotkeyPressed?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    else if (isKeyUpEvent && _isKeyDown)
                    {
                        _isKeyDown = false;
                        var holdDuration = (DateTime.UtcNow - _keyDownTime).TotalMilliseconds;

                        if (holdDuration >= HybridHoldThresholdMs)
                        {
                            // Hold gesture: stop on release.
                            if (_isRecording)
                            {
                                _isRecording = false;
                                _recordingStartedByTap = false;
                                HotkeyReleased?.Invoke(this, EventArgs.Empty);
                            }
                        }
                        else if (!_recordingStartedByTap)
                        {
                            // First quick tap: keep recording (toggle on).
                            _recordingStartedByTap = true;
                        }
                        else
                        {
                            // Second quick tap: toggle off.
                            _isRecording = false;
                            _recordingStartedByTap = false;
                            HotkeyReleased?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }

                return (nint)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void CancelToggle()
    {
        _toggleState = false;
        _isRecording = false;
        _recordingStartedByTap = false;
    }

    public void Dispose()
    {
        UnhookWindowsHookEx(_hookId);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

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
        
        bool ctrlRequired = (TargetModifiers & 1) != 0;
        bool altRequired = (TargetModifiers & 2) != 0;
        bool shiftRequired = (TargetModifiers & 4) != 0;
        bool winRequired = (TargetModifiers & 8) != 0;

        bool ctrlPressed = (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0;
        bool altPressed = (GetAsyncKeyState(VK_LMENU) & 0x8000) != 0 || (GetAsyncKeyState(VK_RMENU) & 0x8000) != 0;
        bool shiftPressed = (GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0;
        bool winPressed = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

        return (!ctrlRequired || ctrlPressed) &&
               (!altRequired || altPressed) &&
               (!shiftRequired || shiftPressed) &&
               (!winRequired || winPressed);
    }
}
