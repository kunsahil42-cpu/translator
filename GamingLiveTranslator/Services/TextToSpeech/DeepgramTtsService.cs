using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.TextToSpeech;

/// <summary>
/// Text-to-Speech service using Deepgram Aura REST API.
/// Only supports English ('en') and Spanish ('es') per verified live documentation.
/// Returns standard 16-bit linear PCM WAV audio bytes.
/// </summary>
public class DeepgramTtsService : ITextToSpeechService
{
    private const string DeepgramEndpoint = "https://api.deepgram.com/v1/speak";
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HashSet<string> _supportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "es"
    };

    private readonly ISecureCredentialStore _credentialStore;

    public string ProviderName => "Deepgram";

    public event EventHandler<TtsErrorEventArgs>? ErrorOccurred;

    public DeepgramTtsService(ISecureCredentialStore credentialStore)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
    }

    public Task<bool> IsLanguageSupportedAsync(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return Task.FromResult(false);

        // Normalize (e.g. en-US -> en)
        var primaryCode = languageCode.Split('-', '_')[0].ToLowerInvariant();
        return Task.FromResult(_supportedLanguages.Contains(primaryCode));
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
            var msg = $"Deepgram Aura does not support language '{languageCode}'. Supported languages are English ('en') and Spanish ('es').";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(msg));
            throw new NotSupportedException(msg);
        }

        var apiKey = await _credentialStore.GetApiKeyAsync("Deepgram");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var msg = "Deepgram API key is not configured. Please enter and save your key in Settings.";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(msg));
            throw new InvalidOperationException(msg);
        }

        var voice = ResolveVoice(options.Voice, primaryLang);
        var url = $"{DeepgramEndpoint}?model={Uri.EscapeDataString(voice)}&encoding=linear16&container=wav";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", apiKey);

            var payload = new { text };
            var json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var errorMsg = $"Deepgram TTS request failed (HTTP {(int)response.StatusCode}): {errorBody}";
                Logger.Error($"Deepgram TTS synthesis failed for model {voice}: HTTP {(int)response.StatusCode}");
                ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(errorMsg));
                throw new HttpRequestException(errorMsg, null, response.StatusCode);
            }

            var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return audioBytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not HttpRequestException && ex is not NotSupportedException && ex is not InvalidOperationException)
        {
            Logger.Error("Unexpected network error calling Deepgram TTS endpoint.", ex);
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs($"Deepgram TTS network error: {ex.Message}", ex));
            throw;
        }
    }

    private static string ResolveVoice(string? requestedVoice, string languageCode)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoice) && requestedVoice.StartsWith("aura-", StringComparison.OrdinalIgnoreCase))
        {
            return requestedVoice;
        }

        return languageCode switch
        {
            "es" => "aura-2-celeste-es",
            _ => "aura-2-asteria-en"
        };
    }
}
