using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceWin.Models;

namespace VoiceWin.Services;

public class GroqTranscriptionService
{
    private static readonly HttpClient _httpClient = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string API_URL = "https://api.groq.com/openai/v1/audio/transcriptions";

    public async Task<TranscriptionResult> TranscribeAsync(byte[] audioData, string apiKey, string model = "whisper-large-v3-turbo", string language = "en")
    {
        var startTime = DateTime.UtcNow;

        try
        {
            using var content = new MultipartFormDataContent();
            
            var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "recording.wav");
            content.Add(new StringContent(model), "model");
            content.Add(new StringContent(language), "language");
            content.Add(new StringContent("json"), "response_format");

            using var request = new HttpRequestMessage(HttpMethod.Post, API_URL);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = content;

            using var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new TranscriptionResult
                {
                    Success = false,
                    Error = $"API Error ({response.StatusCode}): {responseBody}",
                    Duration = DateTime.UtcNow - startTime,
                    Provider = "groq"
                };
            }

            var result = JsonSerializer.Deserialize<GroqResponse>(responseBody, _jsonOptions);

            return new TranscriptionResult
            {
                Success = true,
                Text = result?.Text?.Trim() ?? "",
                Duration = DateTime.UtcNow - startTime,
                Provider = "groq"
            };
        }
        catch (Exception ex)
        {
            return new TranscriptionResult
            {
                Success = false,
                Error = ex.Message,
                Duration = DateTime.UtcNow - startTime,
                Provider = "groq"
            };
        }
    }

    private class GroqResponse
    {
        public string? Text { get; set; }
    }
}
