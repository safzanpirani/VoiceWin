namespace VoiceWin.Models;

public class TranscriptionResult
{
    public bool Success { get; set; }
    public string? Text { get; set; }
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Provider { get; set; }
}
