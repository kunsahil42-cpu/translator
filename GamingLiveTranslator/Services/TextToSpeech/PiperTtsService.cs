using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.TextToSpeech;

/// <summary>
/// Text-to-speech service running locally and offline via bundled Piper-plus.
/// Supports Mandarin Chinese ('zh'), Japanese ('ja'), English ('en'), Spanish ('es'), French ('fr'), Portuguese ('pt').
/// Does NOT support Hindi ('hi'), Arabic ('ar'), or Russian ('ru').
/// </summary>
public class PiperTtsService : ITextToSpeechService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HashSet<string> _supportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "zh", "en", "ja", "es", "fr", "pt", "ko", "sv"
    };

    private readonly PiperProcessManager _processManager;

    public string ProviderName => "Piper";

    public event EventHandler<TtsErrorEventArgs>? ErrorOccurred;

    public PiperTtsService(PiperProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
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
            var msg = $"Offline Piper TTS does not support language '{languageCode}'.";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(msg));
            throw new NotSupportedException(msg);
        }

        var started = await _processManager.EnsureStartedAsync(cancellationToken);
        if (!started)
        {
            var msg = $"Piper offline TTS engine could not be started: {_processManager.StatusMessage}";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(msg));
            throw new InvalidOperationException(msg);
        }

        var voice = ResolveVoice(options.Voice, primaryLang);
        var url = $"http://127.0.0.1:{_processManager.Port}/synthesize";

        try
        {
            var payload = new
            {
                text,
                voice,
                language = primaryLang,
                speed = options.Speed,
                volume = options.Volume
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var notFoundMsg = $"Voice model '{voice}' is not downloaded. Please download it in Settings before enabling voice output.";
                ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(notFoundMsg));
                throw new FileNotFoundException(notFoundMsg);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var errorMsg = $"Piper TTS synthesis failed (HTTP {(int)response.StatusCode}): {body}";
                ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(errorMsg));
                throw new HttpRequestException(errorMsg, null, response.StatusCode);
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not FileNotFoundException && ex is not NotSupportedException && ex is not InvalidOperationException)
        {
            Logger.Error("Communication error with Piper TTS local microservice.", ex);
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs($"Piper TTS error: {ex.Message}", ex));
            throw;
        }
    }

    private static string ResolveVoice(string? requestedVoice, string languageCode)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoice) && !requestedVoice.StartsWith("aura-", StringComparison.OrdinalIgnoreCase))
        {
            return requestedVoice;
        }

        return languageCode switch
        {
            "zh" => "tsukuyomi-chan-6lang-fp16",
            "ja" => "tsukuyomi-chan-6lang-fp16",
            "es" => "tsukuyomi-chan-6lang-fp16",
            _ => "tsukuyomi-chan-6lang-fp16"
        };
    }
}
