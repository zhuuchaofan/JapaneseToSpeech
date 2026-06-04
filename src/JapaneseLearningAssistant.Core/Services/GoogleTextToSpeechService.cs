using System.Net.Http.Json;
using System.Text.Json;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class GoogleTextToSpeechService : ITextToSpeechService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _outputDirectory;

    public GoogleTextToSpeechService(HttpClient httpClient, string apiKey, string outputDirectory)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _outputDirectory = outputDirectory;
    }

    public async Task<AudioGenerationResult> GenerateAsync(TtsRequest request, CancellationToken cancellationToken)
    {
        AppLogger.Info($"Google TTS request started. TextLength={request.Text.Length}, VoiceName={request.VoiceName}, SpeakingRate={request.SpeakingRate}, AudioFormat={request.AudioFormat}.");

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            AppLogger.Error("Google TTS request failed because API key is missing.");
            throw new InvalidOperationException("缺少 Google TTS API Key。请在 appsettings.Local.json 的 googleTtsApiKey 中配置，或设置 GOOGLE_TTS_API_KEY 环境变量。");
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            AppLogger.Error("Google TTS request failed because text is empty.");
            throw new InvalidOperationException("没有可生成语音的日语文本。");
        }

        Directory.CreateDirectory(_outputDirectory);

        var body = new GoogleTtsRequest
        {
            Input = new GoogleTtsInput { Text = request.Text },
            Voice = new GoogleTtsVoice
            {
                LanguageCode = request.LanguageCode,
                Name = request.VoiceName
            },
            AudioConfig = new GoogleTtsAudioConfig
            {
                AudioEncoding = request.AudioFormat,
                SpeakingRate = request.SpeakingRate,
                Pitch = request.Pitch
            }
        };

        var endpoint = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={Uri.EscapeDataString(_apiKey)}";
        using var response = await _httpClient.PostAsJsonAsync(endpoint, body, JsonOptions, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            AppLogger.Error($"Google TTS request failed. StatusCode={response.StatusCode}, Reason={response.ReasonPhrase}.");
            throw new InvalidOperationException(GoogleApiErrorFormatter.Format("Google TTS", response.StatusCode, response.ReasonPhrase, responseText));
        }

        var result = JsonSerializer.Deserialize<GoogleTtsResponse>(responseText, JsonOptions);
        if (string.IsNullOrWhiteSpace(result?.AudioContent))
        {
            AppLogger.Error("Google TTS response did not contain audio content.");
            throw new InvalidOperationException("Google TTS 没有返回音频内容。");
        }

        var extension = request.AudioFormat.Equals("MP3", StringComparison.OrdinalIgnoreCase) ? "mp3" : "audio";
        var filePath = Path.Combine(_outputDirectory, $"tts-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.{extension}");
        await File.WriteAllBytesAsync(filePath, Convert.FromBase64String(result.AudioContent), cancellationToken);
        AppLogger.Info($"Google TTS audio generated. FilePath={filePath}.");

        return new AudioGenerationResult
        {
            FilePath = filePath,
            Provider = "GoogleCloudTextToSpeech",
            VoiceName = request.VoiceName
        };
    }
}

internal sealed class GoogleTtsRequest
{
    public GoogleTtsInput Input { get; set; } = new();
    public GoogleTtsVoice Voice { get; set; } = new();
    public GoogleTtsAudioConfig AudioConfig { get; set; } = new();
}

internal sealed class GoogleTtsInput
{
    public string Text { get; set; } = "";
}

internal sealed class GoogleTtsVoice
{
    public string LanguageCode { get; set; } = "ja-JP";
    public string Name { get; set; } = "";
}

internal sealed class GoogleTtsAudioConfig
{
    public string AudioEncoding { get; set; } = "MP3";
    public double SpeakingRate { get; set; } = 1;
    public double Pitch { get; set; }
}

internal sealed class GoogleTtsResponse
{
    public string AudioContent { get; set; } = "";
}
