using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.TextToSpeech;

/// <summary>
/// Text-to-Speech service using ElevenLabs Cloud Neural TTS API.
/// Powered by the eleven_multilingual_v2 model supporting 29+ languages
/// including Hindi ('hi'), Chinese ('zh'), Japanese ('ja'), and European languages.
/// Standardizes synthesis output to 16-bit linear PCM WAV audio bytes via AudioDecodingHelper.
/// </summary>
public class ElevenLabsTtsService : ITextToSpeechService
{
    private const string BaseTtsEndpoint = "https://api.elevenlabs.io/v1/text-to-speech";
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    // Verified languages natively supported by eleven_multilingual_v2
    private static readonly HashSet<string> _supportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "hi", "zh", "ja", "ko", "es", "fr", "de", "pt", "ru", "ar",
        "it", "pl", "tr", "nl", "sv", "id", "vi", "fil", "uk", "el", "cs",
        "fi", "hr", "ms", "sk", "da", "ta", "bg", "ro", "hu"
    };

    // Default premier voices available across all languages on eleven_multilingual_v2
    public const string DefaultVoiceId = "JBFqnCBsd6RMkjVDRZzb"; // George (Premade - Free Tier & Paid Compatible)

    private readonly ISecureCredentialStore _credentialStore;

    public string ProviderName => "ElevenLabs";

    public event EventHandler<TtsErrorEventArgs>? ErrorOccurred;

    public ElevenLabsTtsService(ISecureCredentialStore credentialStore)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
    }

    public Task<bool> IsLanguageSupportedAsync(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return Task.FromResult(false);

        var primary = languageCode.Split('-', '_')[0].ToLowerInvariant();
        return Task.FromResult(_supportedLanguages.Contains(primary));
    }

    public async Task<byte[]> SynthesizeAsync(
        string text,
        string languageCode,
        TtsOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<byte>();

        var primaryLang = (languageCode ?? "en").Split('-', '_')[0].ToLowerInvariant();
        if (!_supportedLanguages.Contains(primaryLang))
        {
            var msg = $"ElevenLabs does not support language '{languageCode}'.";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(msg));
            throw new NotSupportedException(msg);
        }

        var rawKey = await _credentialStore.GetApiKeyAsync("ElevenLabs");
        var apiKey = ElevenLabsValidator.SanitizeKey(rawKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var msg = "ElevenLabs API key is not configured. Please enter and save your key in Settings.";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(msg));
            throw new InvalidOperationException(msg);
        }

        var voiceId = ResolveVoiceId(options.Voice);
        var url = $"{BaseTtsEndpoint}/{Uri.EscapeDataString(voiceId)}?output_format=mp3_44100_128";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("xi-api-key", apiKey.Trim());

            var payload = new
            {
                text,
                model_id = "eleven_multilingual_v2"
            };

            var json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                string errorMsg;

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    errorMsg = "ElevenLabs authentication failed. Please verify your API key in Settings.";
                }
                else if (response.StatusCode == HttpStatusCode.PaymentRequired || (int)response.StatusCode == 429)
                {
                    errorMsg = "ElevenLabs credit quota exceeded for this billing period or rate limit reached.";
                }
                else
                {
                    errorMsg = $"ElevenLabs synthesis failed (HTTP {(int)response.StatusCode}): {body}";
                }

                Logger.Error($"ElevenLabs TTS synthesis failed (HTTP {(int)response.StatusCode}).");
                ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(errorMsg));
                throw new HttpRequestException(errorMsg, null, response.StatusCode);
            }

            var mp3Bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (mp3Bytes == null || mp3Bytes.Length == 0)
            {
                var errorMsg = "ElevenLabs returned empty audio bytes.";
                ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(errorMsg));
                throw new InvalidOperationException(errorMsg);
            }

            // Decode MP3 to 16-bit linear PCM WAV using NAudio via shared helper
            return AudioDecodingHelper.DecodeMp3ToWavPcm(mp3Bytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not NotSupportedException && ex is not InvalidOperationException && ex is not HttpRequestException)
        {
            Logger.Error("Communication error with ElevenLabs TTS service.", ex);
            var errorMsg = $"ElevenLabs TTS network error: {ex.Message}";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(errorMsg, ex));
            throw new HttpRequestException(errorMsg, ex);
        }
    }

    public static string ResolveVoiceId(string? requestedVoice)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoice))
            return requestedVoice;

        return DefaultVoiceId; // Rachel
    }
}
