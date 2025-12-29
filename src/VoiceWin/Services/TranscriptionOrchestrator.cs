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
    private readonly VadService _vadService;

    private bool _isProcessing;
    private bool _isStreaming;
    private DateTime _streamingStartTime;
    private DateTime _lastSpeechTime;

    public event EventHandler? RecordingStarted;
    public event EventHandler? RecordingStopped;
    public event EventHandler<TranscriptionResult>? TranscriptionCompleted;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<float>? AudioLevelChanged;

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
        _vadService = new VadService();

        _vadService.Initialize();

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
        _lastSpeechTime = DateTime.UtcNow;
        StatusChanged?.Invoke(this, "Recording (streaming)...");
        _audioService.StartRecording();
        RecordingStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnAudioChunkAvailable(object? sender, AudioChunkEventArgs e)
    {
        float level = CalculateAudioLevel(e.Buffer, e.BytesRecorded);
        AudioLevelChanged?.Invoke(this, level);

        if (_isStreaming && _streamingService.IsConnected)
        {
            _streamingService.SendAudio(e.Buffer, e.BytesRecorded);
            
            var settings = _settingsService.Settings;
            if (settings.VadEnabled && level > 0.05f)
            {
                _lastSpeechTime = DateTime.UtcNow;
            }

            var silenceDuration = DateTime.UtcNow - _lastSpeechTime;
            if (settings.VadEnabled && 
                settings.VadStreamingSilenceTimeoutSeconds > 0 &&
                silenceDuration.TotalSeconds >= settings.VadStreamingSilenceTimeoutSeconds)
            {
                Task.Run(() => AutoStopStreamingDueToSilence());
            }
        }
    }

    private void AutoStopStreamingDueToSilence()
    {
        if (!_isStreaming) return;
        
        StatusChanged?.Invoke(this, "Auto-stopped (silence timeout)");
        StopStreamingRecording();
    }

    private static float CalculateAudioLevel(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2) return 0;

        long sum = 0;
        int sampleCount = bytesRecorded / 2;

        for (int i = 0; i < bytesRecorded - 1; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sum += Math.Abs(sample);
        }

        float average = sum / (float)sampleCount;
        return Math.Min(average / 2000f, 1f);
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
            var processedAudio = audioData;

            if (settings.VadEnabled && _vadService.IsInitialized)
            {
                StatusChanged?.Invoke(this, "Detecting speech...");
                processedAudio = _vadService.TrimSilence(
                    audioData,
                    settings.VadThreshold,
                    settings.VadMinSilenceDurationMs);

                if (processedAudio.Length == 0)
                {
                    StatusChanged?.Invoke(this, "No speech detected");
                    return;
                }
            }

            TranscriptionResult result;

            if (settings.TranscriptionProvider == "deepgram" && !string.IsNullOrEmpty(settings.DeepgramApiKey))
            {
                result = await _deepgramService.TranscribeAsync(
                    processedAudio,
                    settings.DeepgramApiKey,
                    settings.DeepgramModel,
                    settings.Language);
            }
            else if (!string.IsNullOrEmpty(settings.GroqApiKey))
            {
                result = await _groqService.TranscribeAsync(
                    processedAudio,
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
        _vadService.Dispose();
    }
}
