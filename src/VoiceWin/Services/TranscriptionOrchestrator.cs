using VoiceWin.Models;

namespace VoiceWin.Services;

public class TranscriptionOrchestrator : IDisposable
{
    private readonly AudioRecordingService _audioService;
    private readonly GroqTranscriptionService _groqService;
    private readonly DeepgramTranscriptionService _deepgramService;
    private readonly DeepgramStreamingService _streamingService;
    private readonly GroqLlmService _llmService;
    private readonly TextPasteService _pasteService;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly SettingsService _settingsService;
    private readonly SoundService _soundService;

    private bool _isProcessing;
    private bool _isStreaming;
    private DateTime _streamingStartTime;

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
        _streamingService = new DeepgramStreamingService();
        _llmService = new GroqLlmService();
        _pasteService = new TextPasteService();
        _hotkeyService = new GlobalHotkeyService();
        _soundService = new SoundService();

        _hotkeyService.TargetVirtualKey = _settingsService.Settings.HotkeyVirtualKey;
        _hotkeyService.Mode = _settingsService.Settings.HotkeyMode;

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.HotkeyReleased += OnHotkeyReleased;

        _audioService.RecordingStarted += (s, e) =>
        {
            if (_settingsService.Settings.PlaySoundFeedback)
                _soundService.PlayStartSound();
            RecordingStarted?.Invoke(this, EventArgs.Empty);
        };
        _audioService.RecordingStopped += (s, e) =>
        {
            if (_settingsService.Settings.PlaySoundFeedback)
                _soundService.PlayEndSound();
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        };
        _audioService.AudioChunkAvailable += OnAudioChunkAvailable;

        _streamingService.TranscriptReceived += OnStreamingTranscriptReceived;
        _streamingService.ErrorOccurred += (s, err) => StatusChanged?.Invoke(this, err);
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_isProcessing || _isStreaming) return;

        var settings = _settingsService.Settings;
        
        if (string.IsNullOrEmpty(settings.GroqApiKey) && string.IsNullOrEmpty(settings.DeepgramApiKey))
        {
            StatusChanged?.Invoke(this, "No API key configured");
            return;
        }

        if (settings.TranscriptionProvider == "deepgram-streaming" && !string.IsNullOrEmpty(settings.DeepgramApiKey))
        {
            StartStreamingRecording();
        }
        else
        {
            StatusChanged?.Invoke(this, "Recording...");
            _audioService.StartRecording();
        }
    }

    private async void StartStreamingRecording()
    {
        var settings = _settingsService.Settings;
        
        StatusChanged?.Invoke(this, "Connecting...");

        string? connectionError = null;
        _streamingService.ErrorOccurred += (s, err) => connectionError = err;
        
        var connected = await _streamingService.ConnectAsync(
            settings.DeepgramApiKey!,
            settings.DeepgramModel,
            settings.Language);

        if (!connected)
        {
            StatusChanged?.Invoke(this, connectionError ?? "Failed to connect to Deepgram");
            return;
        }

        _isStreaming = true;
        _streamingStartTime = DateTime.UtcNow;
        StatusChanged?.Invoke(this, "Recording (streaming)...");
        _audioService.StartRecording();
        RecordingStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnAudioChunkAvailable(object? sender, AudioChunkEventArgs e)
    {
        if (_isStreaming && _streamingService.IsConnected)
        {
            _streamingService.SendAudio(e.Buffer, e.BytesRecorded);
        }
    }

    private async void OnStreamingTranscriptReceived(object? sender, string transcript)
    {
        var settings = _settingsService.Settings;
        var finalText = transcript;

        if (settings.AiEnhancementEnabled && !string.IsNullOrEmpty(settings.GroqApiKey))
        {
            var enhanceResult = await _llmService.EnhanceTextAsync(
                transcript,
                settings.GroqApiKey,
                settings.AiEnhancementPrompt,
                settings.AiEnhancementModel);

            if (enhanceResult.Success && !string.IsNullOrEmpty(enhanceResult.Text))
            {
                finalText = enhanceResult.Text;
            }
        }

        _pasteService.PasteText(finalText);
    }

    private void OnHotkeyReleased(object? sender, EventArgs e)
    {
        if (_isStreaming)
        {
            StopStreamingRecording();
            return;
        }

        if (!_audioService.IsRecording || _isProcessing) return;

        _isProcessing = true;
        
        Task.Run(async () =>
        {
            StatusChanged?.Invoke(this, "Processing...");
            var audioData = _audioService.StopRecording();
            await ProcessTranscriptionAsync(audioData);
        });
    }

    private async void StopStreamingRecording()
    {
        StatusChanged?.Invoke(this, "Finalizing...");
        _audioService.StopRecording();
        
        await _streamingService.CloseAsync();
        
        var duration = DateTime.UtcNow - _streamingStartTime;
        _isStreaming = false;
        
        RecordingStopped?.Invoke(this, EventArgs.Empty);
        StatusChanged?.Invoke(this, $"Streamed in {duration.TotalMilliseconds:F0}ms");
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
                var finalText = result.Text;
                var totalDuration = result.Duration;

                if (settings.AiEnhancementEnabled && !string.IsNullOrEmpty(settings.GroqApiKey))
                {
                    StatusChanged?.Invoke(this, "Enhancing...");
                    
                    var enhanceResult = await _llmService.EnhanceTextAsync(
                        result.Text,
                        settings.GroqApiKey,
                        settings.AiEnhancementPrompt,
                        settings.AiEnhancementModel);

                    if (enhanceResult.Success && !string.IsNullOrEmpty(enhanceResult.Text))
                    {
                        finalText = enhanceResult.Text;
                        totalDuration += enhanceResult.Duration;
                    }
                }

                _pasteService.PasteText(finalText);
                StatusChanged?.Invoke(this, $"Transcribed in {totalDuration.TotalMilliseconds:F0}ms");
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
        _audioService.AudioChunkAvailable -= OnAudioChunkAvailable;
        _hotkeyService.Dispose();
        _audioService.Dispose();
        _streamingService.Dispose();
        _soundService.Dispose();
    }
}
