using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace VoiceWin.Services;

public class DeepgramStreamingService : IDisposable
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private readonly object _lock = new();
    private bool _isConnected;

    public event EventHandler<string>? TranscriptReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? ConnectionClosed;
    public event EventHandler? SpeechStarted;
    public event EventHandler? UtteranceEnded;

    public bool IsConnected => _isConnected;

    public async Task<bool> ConnectAsync(string apiKey, string model = "nova-3", string language = "en")
    {
        try
        {
            _cts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();
            _webSocket.Options.SetRequestHeader("Authorization", $"Token {apiKey}");
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);

            var uri = new Uri($"wss://api.deepgram.com/v1/listen?model={model}&language={language}&encoding=linear16&sample_rate=16000&channels=1&punctuate=true&smart_format=true&interim_results=true&vad_events=true&utterance_end_ms=1000");

            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, connectCts.Token);
            
            await _webSocket.ConnectAsync(uri, linkedCts.Token);
            _isConnected = true;

            _receiveTask = Task.Run(ReceiveLoop);

            return true;
        }
        catch (OperationCanceledException)
        {
            ErrorOccurred?.Invoke(this, "Connection timed out");
            return false;
        }
        catch (WebSocketException wsEx)
        {
            ErrorOccurred?.Invoke(this, $"WebSocket error: {wsEx.Message} (Code: {wsEx.WebSocketErrorCode})");
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Connection failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public void SendAudio(byte[] audioData, int length)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        try
        {
            var segment = new ArraySegment<byte>(audioData, 0, length);
            _webSocket.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Send failed: {ex.Message}");
        }
    }

    public async Task<bool> CloseAsync()
    {
        if (_webSocket?.State != WebSocketState.Open)
            return true;

        try
        {
            var closeMessage = Encoding.UTF8.GetBytes("{\"type\":\"CloseStream\"}");
            await _webSocket.SendAsync(
                new ArraySegment<byte>(closeMessage),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                if (_receiveTask != null)
                    await _receiveTask.WaitAsync(timeoutCts.Token);
            }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }

            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
            }

            _isConnected = false;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Close failed: {ex.Message}");
            _isConnected = false;
            return false;
        }
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[8192];

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !(_cts?.Token.IsCancellationRequested ?? true))
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts!.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessMessage(json);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Receive error: {ex.Message}");
        }
        finally
        {
            _isConnected = false;
            ConnectionClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
                return;

            var type = typeElement.GetString();

            switch (type)
            {
                case "Results":
                    bool isFinal = root.TryGetProperty("is_final", out var finalElement) && finalElement.GetBoolean();
                    if (!isFinal) return;

                    if (root.TryGetProperty("channel", out var channel) &&
                        channel.TryGetProperty("alternatives", out var alternatives) &&
                        alternatives.ValueKind == JsonValueKind.Array &&
                        alternatives.GetArrayLength() > 0 &&
                        alternatives[0].TryGetProperty("transcript", out var transcriptElement))
                    {
                        var transcript = transcriptElement.GetString();
                        if (!string.IsNullOrWhiteSpace(transcript))
                        {
                            TranscriptReceived?.Invoke(this, transcript);
                        }
                    }
                    break;
                case "SpeechStarted":
                    SpeechStarted?.Invoke(this, EventArgs.Empty);
                    break;
                case "UtteranceEnd":
                    UtteranceEnded?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Parse error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _webSocket?.Dispose();
        _cts?.Dispose();
    }
}
