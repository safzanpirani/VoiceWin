using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceWin.Services;

namespace VoiceWin.Views;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly TrayIconService _trayIconService;
    private readonly StatusOverlayWindow _statusOverlay;

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
            Hide();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
