using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WindowsInput;
using WindowsInput.Native;

namespace VoiceWin.Services;

public class TextPasteService
{
    private readonly InputSimulator _inputSimulator;
    private readonly BlockingCollection<string> _pasteQueue = new();
    private readonly Thread _pasteThread;

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    public TextPasteService()
    {
        _inputSimulator = new InputSimulator();
        
        // Single dedicated STA thread for all paste operations - ensures ordering
        _pasteThread = new Thread(PasteWorker);
        _pasteThread.SetApartmentState(ApartmentState.STA);
        _pasteThread.IsBackground = true;
        _pasteThread.Start();
    }

    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var finalText = text.EndsWith(" ") ? text : text + " ";
        _pasteQueue.Add(finalText);
    }

    private void PasteWorker()
    {
        foreach (var text in _pasteQueue.GetConsumingEnumerable())
        {
            SetClipboardText(text);
            Thread.Sleep(50);
            _inputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
            Thread.Sleep(150);
        }
    }

    private void SetClipboardText(string text)
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var bytes = (text.Length + 1) * 2;
                    var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                    var target = GlobalLock(hGlobal);
                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    GlobalUnlock(hGlobal);
                    SetClipboardData(CF_UNICODETEXT, hGlobal);
                    return;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(30);
        }
    }
}
