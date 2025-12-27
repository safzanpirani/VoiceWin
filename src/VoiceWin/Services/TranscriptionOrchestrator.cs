using VoiceWin.Models;

namespace VoiceWin.Services;

public class TranscriptionOrchestrator : IDisposable
{
    private readonly AudioRecordingService _audioService;
    private readonly GroqTranscriptionService _groqService;
    private readonly DeepgramTranscriptionService _deepgramService;
    private readonly TextPasteService _pasteService;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly SettingsService _settingsService;

    private bool _isProcessing;

    public event EventHandler? RecordingStarted;
    public event EventHandler? RecordingStopped;
    public event EventHandler<TranscriptionResult>? TranscriptionCompleted;
    public event EventHandler<string>? StatusChanged;

    public bool IsRecording => _audioService.IsRecording;

    public TranscriptionOrchestrator(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _audioService = new AudioRecordingService();
        _groqService = new GroqTranscriptionService();
        _deepgramService = new DeepgramTranscriptionService();
        _pasteService = new TextPasteService();
        _hotkeyService = new GlobalHotkeyService();

        _hotkeyService.TargetVirtualKey = _settingsService.Settings.HotkeyVirtualKey;
        _hotkeyService.Mode = _settingsService.Settings.HotkeyMode;

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.HotkeyReleased += OnHotkeyReleased;

        _audioService.RecordingStarted += (s, e) => RecordingStarted?.Invoke(this, EventArgs.Empty);
        _audioService.RecordingStopped += (s, e) => RecordingStopped?.Invoke(this, EventArgs.Empty);
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_isProcessing) return;

        var settings = _settingsService.Settings;
        
        if (string.IsNullOrEmpty(settings.GroqApiKey) && string.IsNullOrEmpty(settings.DeepgramApiKey))
        {
            StatusChanged?.Invoke(this, "No API key configured");
            return;
        }

        StatusChanged?.Invoke(this, "Recording...");
        _audioService.StartRecording();
    }

    private void OnHotkeyReleased(object? sender, EventArgs e)
    {
        if (!_audioService.IsRecording || _isProcessing) return;

        _isProcessing = true;
        
        Task.Run(async () =>
        {
            StatusChanged?.Invoke(this, "Processing...");
            var audioData = _audioService.StopRecording();
            await ProcessTranscriptionAsync(audioData);
        });
    }

    private async Task ProcessTranscriptionAsync(byte[] audioData)
    {
        try
        {
            if (audioData.Length < 1000)
            {
                StatusChanged?.Invoke(this, "Recording too short");
                return;
            }

            var settings = _settingsService.Settings;
            TranscriptionResult result;

            if (settings.TranscriptionProvider == "deepgram" && !string.IsNullOrEmpty(settings.DeepgramApiKey))
            {
                result = await _deepgramService.TranscribeAsync(
                    audioData,
                    settings.DeepgramApiKey,
                    settings.DeepgramModel,
                    settings.Language);
            }
            else if (!string.IsNullOrEmpty(settings.GroqApiKey))
            {
                result = await _groqService.TranscribeAsync(
                    audioData,
                    settings.GroqApiKey,
                    settings.GroqModel,
                    settings.Language);
            }
            else
            {
                StatusChanged?.Invoke(this, "No valid API key for selected provider");
                return;
            }

            TranscriptionCompleted?.Invoke(this, result);

            if (result.Success && !string.IsNullOrEmpty(result.Text))
            {
                _pasteService.PasteText(result.Text);
                StatusChanged?.Invoke(this, $"Transcribed in {result.Duration.TotalMilliseconds:F0}ms");
            }
            else
            {
                StatusChanged?.Invoke(this, result.Error ?? "Transcription failed");
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    public void UpdateHotkeySettings()
    {
        _hotkeyService.TargetVirtualKey = _settingsService.Settings.HotkeyVirtualKey;
        _hotkeyService.Mode = _settingsService.Settings.HotkeyMode;
    }

    public void Dispose()
    {
        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _hotkeyService.HotkeyReleased -= OnHotkeyReleased;
        _hotkeyService.Dispose();
        _audioService.Dispose();
    }
}
