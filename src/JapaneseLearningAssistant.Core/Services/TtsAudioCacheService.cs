using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class TtsAudioCacheService : ITextToSpeechService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ITextToSpeechService _inner;
    private readonly string _cacheDirectory;
    private readonly string _providerName;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public TtsAudioCacheService(ITextToSpeechService inner, string cacheDirectory, string providerName)
    {
        _inner = inner;
        _cacheDirectory = cacheDirectory;
        _providerName = providerName;
    }

    public async Task<AudioGenerationResult> GenerateAsync(TtsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new InvalidOperationException("No text was provided for speech generation.");
        }

        Directory.CreateDirectory(_cacheDirectory);

        var cacheKey = CreateCacheKey(request);
        var extension = GetExtension(request.AudioFormat);
        var audioPath = Path.Combine(_cacheDirectory, $"{cacheKey}.{extension}");
        var metadataPath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");

        if (File.Exists(audioPath))
        {
            return CreateCachedResult(audioPath, request);
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(audioPath))
            {
                return CreateCachedResult(audioPath, request);
            }

            var generated = await _inner.GenerateAsync(request, cancellationToken);
            File.Copy(generated.FilePath, audioPath, overwrite: false);

            var metadata = new TtsAudioCacheMetadata
            {
                CacheKey = cacheKey,
                Provider = _providerName,
                CreatedAt = DateTimeOffset.Now,
                LanguageCode = request.LanguageCode,
                VoiceName = request.VoiceName,
                SpeakingRate = request.SpeakingRate,
                Pitch = request.Pitch,
                AudioFormat = request.AudioFormat,
                Text = request.Text
            };

            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);
            DeleteGeneratedTemporaryFile(generated.FilePath, audioPath);

            return CreateCachedResult(audioPath, request);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private AudioGenerationResult CreateCachedResult(string audioPath, TtsRequest request)
    {
        return new AudioGenerationResult
        {
            FilePath = audioPath,
            Provider = _providerName,
            VoiceName = request.VoiceName
        };
    }

    private string CreateCacheKey(TtsRequest request)
    {
        var key = new TtsAudioCacheKey
        {
            Provider = _providerName,
            Text = request.Text.Trim(),
            LanguageCode = request.LanguageCode,
            VoiceName = request.VoiceName,
            SpeakingRate = request.SpeakingRate.ToString("0.###", CultureInfo.InvariantCulture),
            Pitch = request.Pitch.ToString("0.###", CultureInfo.InvariantCulture),
            AudioFormat = request.AudioFormat.ToUpperInvariant()
        };

        var json = JsonSerializer.Serialize(key, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetExtension(string audioFormat)
    {
        return audioFormat.Equals("MP3", StringComparison.OrdinalIgnoreCase) ? "mp3" : "audio";
    }

    private static void DeleteGeneratedTemporaryFile(string generatedPath, string cachePath)
    {
        if (string.Equals(Path.GetFullPath(generatedPath), Path.GetFullPath(cachePath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(generatedPath))
            {
                File.Delete(generatedPath);
            }
        }
        catch
        {
            // Temporary cleanup is best effort; the cached audio is already available.
        }
    }

    private sealed class TtsAudioCacheKey
    {
        public string Provider { get; init; } = "";
        public string Text { get; init; } = "";
        public string LanguageCode { get; init; } = "";
        public string VoiceName { get; init; } = "";
        public string SpeakingRate { get; init; } = "";
        public string Pitch { get; init; } = "";
        public string AudioFormat { get; init; } = "";
    }

    private sealed class TtsAudioCacheMetadata
    {
        public string CacheKey { get; init; } = "";
        public string Provider { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public string LanguageCode { get; init; } = "";
        public string VoiceName { get; init; } = "";
        public double SpeakingRate { get; init; }
        public double Pitch { get; init; }
        public string AudioFormat { get; init; } = "";
        public string Text { get; init; } = "";
    }
}
