using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceWin.Services;

public class GroqLlmService
{
    private static readonly HttpClient _httpClient = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private const string API_URL = "https://api.groq.com/openai/v1/chat/completions";

    public async Task<LlmResult> EnhanceTextAsync(string text, string apiKey, string systemPrompt, string model)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var userContent = $"<TRANSCRIPT>\n{text}\n</TRANSCRIPT>";

            var requestBody = new ChatCompletionRequest
            {
                Model = model,
                Messages = new[]
                {
                    new Message { Role = "system", Content = systemPrompt },
                    new Message { Role = "user", Content = userContent }
                },
                Temperature = 0.3,
                MaxTokens = 4096
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, API_URL);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = content;

            using var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new LlmResult
                {
                    Success = false,
                    Error = $"API Error ({response.StatusCode}): {responseBody}",
                    Duration = DateTime.UtcNow - startTime
                };
            }

            var result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, _jsonOptions);
            var enhancedText = (result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "") + " ";

            return new LlmResult
            {
                Success = true,
                Text = enhancedText,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            return new LlmResult
            {
                Success = false,
                Error = ex.Message,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    private class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public Message[] Messages { get; set; } = Array.Empty<Message>();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
    }

    private class Message
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private class ChatCompletionResponse
    {
        public Choice[]? Choices { get; set; }
    }

    private class Choice
    {
        public ResponseMessage? Message { get; set; }
    }

    private class ResponseMessage
    {
        public string? Content { get; set; }
    }
}

public class LlmResult
{
    public bool Success { get; set; }
    public string? Text { get; set; }
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
}
