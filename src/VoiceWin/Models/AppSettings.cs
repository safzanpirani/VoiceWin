namespace VoiceWin.Models;

public class AppSettings
{
    public string? GroqApiKey { get; set; }
    public string? DeepgramApiKey { get; set; }
    public string TranscriptionProvider { get; set; } = "groq";
    public string GroqModel { get; set; } = "whisper-large-v3-turbo";
    public string DeepgramModel { get; set; } = "nova-3";
    public string HotkeyMode { get; set; } = "hold";
    public int HotkeyVirtualKey { get; set; } = 165;
    public string Language { get; set; } = "en";
    public bool PlaySoundFeedback { get; set; } = true;
    public bool ShowRecordingOverlay { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = true;
}
