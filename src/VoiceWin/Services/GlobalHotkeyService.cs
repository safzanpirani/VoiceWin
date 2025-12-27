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
    public string Mode { get; set; } = "hold";
    
    private bool _toggleState;
    
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

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

            if (vkCode == TargetVirtualKey)
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

                return (nint)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
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
}
