using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VoiceWin.Services;

namespace VoiceWin.Views;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly TrayIconService _trayIconService;
    private readonly StatusOverlayWindow _statusOverlay;
    private bool _isRecordingHotkey;
    private int _pendingHotkeyVirtualKey;
    private int _pendingHotkeyModifiers;
    private int _recordingModifierMask;

    private const int ModifierCtrl = 1;
    private const int ModifierAlt = 2;
    private const int ModifierShift = 4;
    private const int ModifierWin = 8;
    private const int ModifierMouseLeft = 16;
    private const int ModifierMouseRight = 32;
    private const int ModifierMouseMiddle = 64;
    private const int ModifierMouseX1 = 128;
    private const int ModifierMouseX2 = 256;

    private const int VK_SHIFT = 16;
    private const int VK_CONTROL = 17;
    private const int VK_MENU = 18;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_MBUTTON = 0x04;
    private const int VK_XBUTTON1 = 0x05;
    private const int VK_XBUTTON2 = 0x06;

    public MainWindow()
    {
        InitializeComponent();
        _app = (App)Application.Current;
        _trayIconService = new TrayIconService();
        _statusOverlay = new StatusOverlayWindow();

        LoadSettings();
        SubscribeToEvents();

        TaskbarIcon.IconSource = _trayIconService.CreateIconWithStatus(TrayStatus.Ready);
    }

    private void LoadSettings()
    {
        var settings = _app.SettingsService.Settings;

        GroqApiKeyBox.Text = settings.GroqApiKey ?? "";
        DeepgramApiKeyBox.Text = settings.DeepgramApiKey ?? "";

        SelectComboItemByTag(ProviderCombo, settings.TranscriptionProvider);
        SelectComboItemByTag(HotkeyModeCombo, settings.HotkeyMode);
        SelectComboItemByTag(OverlayPositionCombo, settings.OverlayPosition);
        SelectComboItemByTag(LanguageCombo, settings.Language);

        AiEnhancementCheckBox.IsChecked = settings.AiEnhancementEnabled;
        AiEnhancementPromptBox.Text = settings.AiEnhancementPrompt;

        VadEnabledCheckBox.IsChecked = settings.VadEnabled;
        VadSilenceTimeoutBox.Text = settings.VadStreamingSilenceTimeoutSeconds.ToString();

        _pendingHotkeyVirtualKey = settings.HotkeyVirtualKey;
        _pendingHotkeyModifiers = settings.HotkeyModifiers;
        HotkeyDisplayBox.Text = GetHotkeyDisplayString(_pendingHotkeyModifiers, _pendingHotkeyVirtualKey);

        _statusOverlay.SetPosition(settings.OverlayPosition);
    }

    private void SelectComboItemByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private string GetSelectedTag(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
    }

    private void SubscribeToEvents()
    {
        _app.Orchestrator.RecordingStarted += (s, e) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                StatusText.Text = "Recording...";
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                TaskbarIcon.ToolTipText = "VoiceWin - Recording...";
                TaskbarIcon.IconSource = _trayIconService.CreateIconWithStatus(TrayStatus.Recording);
                
                bool isStreaming = _app.SettingsService.Settings.TranscriptionProvider == "deepgram-streaming";
                _statusOverlay.SetMode(isStreaming);
                _statusOverlay.UpdateStatus("Recording");
                _statusOverlay.StartAnimating();
            });
        };

        _app.Orchestrator.RecordingStopped += (s, e) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                StatusText.Text = "Processing...";
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(234, 179, 8));
                TaskbarIcon.IconSource = _trayIconService.CreateIconWithStatus(TrayStatus.Processing);
                
                _statusOverlay.StopAnimating();
                _statusOverlay.UpdateStatus("Processing");
            });
        };

        _app.Orchestrator.StatusChanged += (s, status) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                StatusText.Text = status;
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                TaskbarIcon.ToolTipText = $"VoiceWin - {status}";
                TaskbarIcon.IconSource = _trayIconService.CreateIconWithStatus(TrayStatus.Ready);
                
                _statusOverlay.UpdateStatus(status);
                if (IsTerminalStatus(status))
                {
                    _statusOverlay.HideOverlay();
                }
            });
        };

        _app.Orchestrator.TranscriptionCompleted += (s, result) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (result.Success)
                {
                    StatusText.Text = $"Done ({result.Duration.TotalMilliseconds:F0}ms)";
                }
            });
        };

        _app.Orchestrator.AudioLevelChanged += (s, level) =>
        {
            _statusOverlay.UpdateAudioLevel(level);
        };

        _app.Orchestrator.SpeechDetected += (s, isSpeaking) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _statusOverlay.UpdateStatus(isSpeaking ? "Recording" : "Listening");
            });
        };
    }

    private static bool IsTerminalStatus(string status)
    {
        return status.Contains("Transcribed") || 
               status.Contains("Streamed") || 
               status.Contains("Error") ||
               status.Contains("too short") ||
               status.Contains("No API") ||
               status.Contains("No valid") ||
               status.Contains("No speech") ||
               status.Contains("Auto-stopped");
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _app.SettingsService.UpdateSettings(settings =>
        {
            settings.GroqApiKey = GroqApiKeyBox.Text.Trim();
            settings.DeepgramApiKey = DeepgramApiKeyBox.Text.Trim();
            settings.TranscriptionProvider = GetSelectedTag(ProviderCombo);
            settings.HotkeyMode = GetSelectedTag(HotkeyModeCombo);
            settings.OverlayPosition = GetSelectedTag(OverlayPositionCombo);
            settings.Language = GetSelectedTag(LanguageCombo);
            settings.AiEnhancementEnabled = AiEnhancementCheckBox.IsChecked ?? false;
            settings.AiEnhancementPrompt = AiEnhancementPromptBox.Text;
            settings.VadEnabled = VadEnabledCheckBox.IsChecked ?? true;
            if (int.TryParse(VadSilenceTimeoutBox.Text, out int timeout))
            {
                settings.VadStreamingSilenceTimeoutSeconds = timeout;
            }
            settings.HotkeyVirtualKey = _pendingHotkeyVirtualKey;
            settings.HotkeyModifiers = _pendingHotkeyModifiers;
        });

        _app.Orchestrator.UpdateHotkeySettings();
        _statusOverlay.SetPosition(_app.SettingsService.Settings.OverlayPosition);

        StatusText.Text = "Settings saved!";
    }

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void TaskbarIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            CancelHotkeyRecording();
            Hide();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CancelHotkeyRecording();
        e.Cancel = true;
        Hide();
    }

    private void RecordHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecordingHotkey)
        {
            CancelHotkeyRecording();
            return;
        }

        _isRecordingHotkey = true;
        _app.Orchestrator.SetHotkeyEnabled(false);
        RecordHotkeyButton.Content = "Cancel";
        _recordingModifierMask = 0;
        HotkeyDisplayBox.Text = "Press key or mouse combo...";
        HotkeyDisplayBox.Focus();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (!_isRecordingHotkey) return;

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vkCode = KeyInterop.VirtualKeyFromKey(key);
        if (vkCode == 0) return;

        TryCaptureHotkeyOnDown(vkCode, allowModifierOnlyCombo: false);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);

        if (!_isRecordingHotkey) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vkCode = KeyInterop.VirtualKeyFromKey(key);
        if (TryCaptureSingleModifierOnUp(vkCode))
        {
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);

        if (!_isRecordingHotkey) return;
        if (IsEventFromRecordButton(e.OriginalSource)) return;

        int vkCode = GetMouseVirtualKey(e.ChangedButton);
        if (vkCode == 0) return;

        e.Handled = true;
        TryCaptureHotkeyOnDown(vkCode, allowModifierOnlyCombo: true);
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);

        if (!_isRecordingHotkey) return;
        if (IsEventFromRecordButton(e.OriginalSource)) return;

        int vkCode = GetMouseVirtualKey(e.ChangedButton);
        if (vkCode == 0) return;

        if (TryCaptureSingleModifierOnUp(vkCode))
        {
            e.Handled = true;
        }
    }

    private void TryCaptureHotkeyOnDown(int vkCode, bool allowModifierOnlyCombo)
    {
        int modifiers = GetCurrentModifierMask();

        if (IsModifierKey(vkCode))
        {
            int modifierBit = GetModifierBit(vkCode);
            int otherModifiers = modifiers & ~modifierBit;

            if (allowModifierOnlyCombo && otherModifiers != 0)
            {
                CommitRecordedHotkey(otherModifiers, vkCode);
                return;
            }

            _recordingModifierMask = modifiers;
            string modifierString = GetModifierString(modifiers);
            HotkeyDisplayBox.Text = string.IsNullOrEmpty(modifierString) ? "..." : $"{modifierString}...";
            return;
        }

        CommitRecordedHotkey(modifiers, vkCode);
    }

    private bool TryCaptureSingleModifierOnUp(int vkCode)
    {
        if (!IsModifierKey(vkCode))
        {
            return false;
        }

        int releasedModifierBit = GetModifierBit(vkCode);
        int currentModifiers = GetCurrentModifierMask();

        if (_recordingModifierMask == releasedModifierBit && currentModifiers == 0)
        {
            CommitRecordedHotkey(0, vkCode);
            return true;
        }

        return false;
    }

    private void CommitRecordedHotkey(int modifiers, int vkCode)
    {
        _pendingHotkeyModifiers = modifiers;
        _pendingHotkeyVirtualKey = vkCode;
        HotkeyDisplayBox.Text = GetHotkeyDisplayString(modifiers, vkCode);
        _isRecordingHotkey = false;
        _recordingModifierMask = 0;
        _app.Orchestrator.SetHotkeyEnabled(true);
        RecordHotkeyButton.Content = "Record";
    }

    private void CancelHotkeyRecording()
    {
        if (!_isRecordingHotkey)
        {
            return;
        }

        _isRecordingHotkey = false;
        _recordingModifierMask = 0;
        _app.Orchestrator.SetHotkeyEnabled(true);
        RecordHotkeyButton.Content = "Record";
        HotkeyDisplayBox.Text = GetHotkeyDisplayString(_pendingHotkeyModifiers, _pendingHotkeyVirtualKey);
    }

    private bool IsEventFromRecordButton(object? source)
    {
        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        DependencyObject? current = dependencyObject;
        while (current != null)
        {
            if (ReferenceEquals(current, RecordHotkeyButton))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static int GetCurrentModifierMask()
    {
        int modifiers = 0;

        if ((GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0) modifiers |= ModifierCtrl;
        if ((GetAsyncKeyState(VK_LMENU) & 0x8000) != 0 || (GetAsyncKeyState(VK_RMENU) & 0x8000) != 0) modifiers |= ModifierAlt;
        if ((GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0) modifiers |= ModifierShift;
        if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0) modifiers |= ModifierWin;
        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0) modifiers |= ModifierMouseLeft;
        if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0) modifiers |= ModifierMouseRight;
        if ((GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0) modifiers |= ModifierMouseMiddle;
        if ((GetAsyncKeyState(VK_XBUTTON1) & 0x8000) != 0) modifiers |= ModifierMouseX1;
        if ((GetAsyncKeyState(VK_XBUTTON2) & 0x8000) != 0) modifiers |= ModifierMouseX2;

        return modifiers;
    }

    private static bool IsModifierKey(int vkCode)
    {
        return GetModifierBit(vkCode) != 0;
    }

    private static int GetModifierBit(int vkCode)
    {
        return vkCode switch
        {
            VK_SHIFT or VK_LSHIFT or VK_RSHIFT => ModifierShift,
            VK_CONTROL or VK_LCONTROL or VK_RCONTROL => ModifierCtrl,
            VK_MENU or VK_LMENU or VK_RMENU => ModifierAlt,
            VK_LWIN or VK_RWIN => ModifierWin,
            VK_LBUTTON => ModifierMouseLeft,
            VK_RBUTTON => ModifierMouseRight,
            VK_MBUTTON => ModifierMouseMiddle,
            VK_XBUTTON1 => ModifierMouseX1,
            VK_XBUTTON2 => ModifierMouseX2,
            _ => 0
        };
    }

    private static int GetMouseVirtualKey(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => VK_LBUTTON,
            MouseButton.Right => VK_RBUTTON,
            MouseButton.Middle => VK_MBUTTON,
            MouseButton.XButton1 => VK_XBUTTON1,
            MouseButton.XButton2 => VK_XBUTTON2,
            _ => 0
        };
    }

    private static string GetModifierString(int modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModifierCtrl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierShift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierWin) != 0) parts.Add("Win");
        if ((modifiers & ModifierMouseLeft) != 0) parts.Add("Left Mouse");
        if ((modifiers & ModifierMouseRight) != 0) parts.Add("Right Mouse");
        if ((modifiers & ModifierMouseMiddle) != 0) parts.Add("Middle Mouse");
        if ((modifiers & ModifierMouseX1) != 0) parts.Add("Mouse X1");
        if ((modifiers & ModifierMouseX2) != 0) parts.Add("Mouse X2");
        return parts.Count > 0 ? string.Join(" + ", parts) : "";
    }

    private static string GetHotkeyDisplayString(int modifiers, int virtualKey)
    {
        var modStr = GetModifierString(modifiers);
        var keyStr = GetKeyName(virtualKey);
        return string.IsNullOrEmpty(modStr) ? keyStr : $"{modStr} + {keyStr}";
    }

    private static string GetKeyName(int virtualKey)
    {
        return virtualKey switch
        {
            VK_LBUTTON => "Left Mouse",
            VK_RBUTTON => "Right Mouse",
            VK_MBUTTON => "Middle Mouse",
            VK_XBUTTON1 => "Mouse X1",
            VK_XBUTTON2 => "Mouse X2",
            8 => "Backspace",
            9 => "Tab",
            13 => "Enter",
            16 => "Shift",
            17 => "Ctrl",
            18 => "Alt",
            19 => "Pause",
            20 => "Caps Lock",
            27 => "Escape",
            32 => "Space",
            33 => "Page Up",
            34 => "Page Down",
            35 => "End",
            36 => "Home",
            37 => "Left Arrow",
            38 => "Up Arrow",
            39 => "Right Arrow",
            40 => "Down Arrow",
            45 => "Insert",
            46 => "Delete",
            91 => "Left Win",
            92 => "Right Win",
            93 => "Menu",
            112 => "F1",
            113 => "F2",
            114 => "F3",
            115 => "F4",
            116 => "F5",
            117 => "F6",
            118 => "F7",
            119 => "F8",
            120 => "F9",
            121 => "F10",
            122 => "F11",
            123 => "F12",
            144 => "Num Lock",
            145 => "Scroll Lock",
            160 => "Left Shift",
            161 => "Right Shift",
            162 => "Left Ctrl",
            163 => "Right Ctrl",
            164 => "Left Alt",
            165 => "Right Alt",
            186 => ";",
            187 => "=",
            188 => ",",
            189 => "-",
            190 => ".",
            191 => "/",
            192 => "`",
            219 => "[",
            220 => "\\",
            221 => "]",
            222 => "'",
            _ when virtualKey >= 48 && virtualKey <= 57 => ((char)virtualKey).ToString(),
            _ when virtualKey >= 65 && virtualKey <= 90 => ((char)virtualKey).ToString(),
            _ when virtualKey >= 96 && virtualKey <= 105 => $"Numpad {virtualKey - 96}",
            _ => $"Key {virtualKey}"
        };
    }
}
