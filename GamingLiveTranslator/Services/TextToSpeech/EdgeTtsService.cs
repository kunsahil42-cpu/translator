using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Utilities;
using NAudio.Wave;

namespace GamingLiveTranslator.Services.TextToSpeech;

/// <summary>
/// Text-to-speech service using Microsoft Edge's online neural Read Aloud service via local microservice.
/// Free, zero API key, supports 100+ languages including Hindi ('hi') and Chinese ('zh').
/// Standardizes synthesis output to 16-bit linear PCM WAV audio bytes via NAudio decoding.
/// </summary>
public class EdgeTtsService : ITextToSpeechService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly PiperProcessManager _processManager;

    public string ProviderName => "Edge TTS";

    public event EventHandler<TtsErrorEventArgs>? ErrorOccurred;

    // Real, verified neural voice catalog for supported target languages
    private static readonly HashSet<string> _supportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "hi", "zh", "ja", "ko", "en", "es", "fr", "de", "pt", "ru", "ar"
    };

    public EdgeTtsService(PiperProcessManager processManager)
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
            var msg = $"Edge TTS does not support language '{languageCode}'.";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(msg));
            throw new NotSupportedException(msg);
        }

        var started = await _processManager.EnsureStartedAsync(cancellationToken);
        if (!started)
        {
            var msg = $"Local TTS runtime service could not be started: {_processManager.StatusMessage}";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(msg));
            throw new InvalidOperationException(msg);
        }

        var voice = ResolveVoice(options.Voice, primaryLang);
        var url = $"http://127.0.0.1:{_processManager.Port}/synthesize";

        try
        {
            var payload = new
            {
                engine = "edge",
                text,
                voice,
                language = primaryLang,
                speed = options.Speed,
                volume = options.Volume
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var is503 = response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable;
                var errorMsg = is503
                    ? "Edge TTS (unofficial) is currently unavailable. Microsoft's endpoint may be unreachable, rate-limited, or blocked. Please switch to Piper (offline) or Deepgram."
                    : $"Edge TTS synthesis failed (HTTP {(int)response.StatusCode}): {body}";

                Logger.Error($"Edge TTS synthesis failure: {errorMsg}");
                ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(errorMsg));
                throw new HttpRequestException(errorMsg, null, response.StatusCode);
            }

            var mp3Bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (mp3Bytes == null || mp3Bytes.Length == 0)
            {
                var errorMsg = "Edge TTS returned empty audio bytes.";
                ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(errorMsg));
                throw new InvalidOperationException(errorMsg);
            }

            // Decode MP3 to 16-bit linear PCM WAV using NAudio so downstream normalization & playback receive standard WAV
            return DecodeMp3ToWavPcm(mp3Bytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not NotSupportedException && ex is not InvalidOperationException && ex is not HttpRequestException)
        {
            Logger.Error("Communication error with Edge TTS local microservice.", ex);
            var errorMsg = "Edge TTS (unofficial) is currently unavailable. Could not communicate with local runtime.";
            ErrorOccurred?.Invoke(this, new TtsErrorEventArgs(errorMsg, ex));
            throw new HttpRequestException(errorMsg, ex);
        }
    }

    /// <summary>
    /// Decodes raw MP3 stream bytes into standard 16-bit linear PCM WAV bytes.
    /// Uses Windows Media Foundation via NAudio, guaranteeing full compatibility
    /// with the downstream peak normalization, presence EQ, and WASAPI dual-output pipeline.
    /// </summary>
    public static byte[] DecodeMp3ToWavPcm(byte[] mp3Bytes)
    {
        using var mp3Stream = new MemoryStream(mp3Bytes);
        using var reader = new StreamMediaFoundationReader(mp3Stream);
        using var wavStream = new MemoryStream();
        using (var writer = new WaveFileWriter(wavStream, reader.WaveFormat))
        {
            reader.CopyTo(writer);
        }
        return wavStream.ToArray();
    }

    public static string ResolveVoice(string? requestedVoice, string languageCode)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoice) && requestedVoice.Contains("Neural", StringComparison.OrdinalIgnoreCase))
        {
            return requestedVoice;
        }

        return languageCode switch
        {
            "hi" => "hi-IN-SwaraNeural",
            "zh" => "zh-CN-XiaoxiaoNeural",
            "ja" => "ja-JP-NanamiNeural",
            "ko" => "ko-KR-SunHiNeural",
            "es" => "es-ES-ElviraNeural",
            "fr" => "fr-FR-DeniseNeural",
            "de" => "de-DE-KatjaNeural",
            "pt" => "pt-BR-FranciscaNeural",
            "ru" => "ru-RU-SvetlanaNeural",
            "ar" => "ar-SA-ZariyahNeural",
            _ => "en-US-JennyNeural"
        };
    }
}