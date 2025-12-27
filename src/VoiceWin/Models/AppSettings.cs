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
    
    // AI Enhancement
    public bool AiEnhancementEnabled { get; set; } = false;
    public string AiEnhancementPrompt { get; set; } = @"- Rewrite the <TRANSCRIPT> text with enhanced clarity, improved sentence structure, and rhythmic flow while preserving the original meaning and tone.
- Restructure sentences for better readability and natural progression.
- Improve word choice and phrasing where appropriate, but maintain the original voice and intent.
- Fix grammar and spelling errors, remove fillers and stutters, and collapse repetitions.
- Format any lists as proper bullet points or numbered lists.
- Write numbers as numerals (e.g., 'five' → '5', 'twenty dollars' → '$20').
- Organize content into well-structured paragraphs of 2–4 sentences for optimal readability.
- Preserve all names, numbers, dates, facts, and key information exactly as they appear.
- Do not add explanations, labels, metadata, or instructions.
- Output only the rewritten text.
- Don't add any information not available in the <TRANSCRIPT> text ever.
- Use all lowercase letters. No capitalization at all, including the start of sentences.
- Minimize punctuation. Use commas where needed, but avoid periods unless absolutely necessary for clarity. Keep it casual.";
    public string AiEnhancementModel { get; set; } = "moonshotai/kimi-k2-instruct-0905";
}
