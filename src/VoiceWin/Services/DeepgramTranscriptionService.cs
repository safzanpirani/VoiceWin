using Deepgram;
using Deepgram.Models.Listen.v1.REST;
using VoiceWin.Models;

namespace VoiceWin.Services;

public class DeepgramTranscriptionService
{
    private static bool _initialized;
    private static readonly object _lock = new();

    public async Task<TranscriptionResult> TranscribeAsync(byte[] audioData, string apiKey, string model = "nova-3", string language = "en")
    {
        var startTime = DateTime.UtcNow;

        try
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    Environment.SetEnvironmentVariable("DEEPGRAM_API_KEY", apiKey);
                    Library.Initialize();
                    _initialized = true;
                }
            }

            var listenClient = ClientFactory.CreateListenRESTClient();

            var response = await listenClient.TranscribeFile(
                audioData,
                new PreRecordedSchema
                {
                    Model = model,
                    Language = language,
                    Punctuate = true,
                    SmartFormat = true
                });

            var transcript = response?.Results?.Channels?
                .FirstOrDefault()?
                .Alternatives?
                .FirstOrDefault()?
                .Transcript ?? "";

            return new TranscriptionResult
            {
                Success = true,
                Text = transcript.Trim(),
                Duration = DateTime.UtcNow - startTime,
                Provider = "deepgram"
            };
        }
        catch (Exception ex)
        {
            return new TranscriptionResult
            {
                Success = false,
                Error = ex.Message,
                Duration = DateTime.UtcNow - startTime,
                Provider = "deepgram"
            };
        }
    }
}
