using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.TextToSpeech;

/// <summary>
/// Composite text-to-speech service routing synthesis requests to the user's
/// preferred provider (Deepgram Aura, Piper-plus, Edge TTS, or ElevenLabs) based on application settings.
/// </summary>
public class TextToSpeechService : ITextToSpeechService
{
    private readonly DeepgramTtsService _deepgramTts;
    private readonly PiperTtsService _piperTts;
    private readonly EdgeTtsService _edgeTts;
    private readonly ElevenLabsTtsService _elevenLabsTts;
    private readonly SettingsService _settingsService;

    public string ProviderName => "CompositeTts";

    public event EventHandler<TtsErrorEventArgs>? ErrorOccurred;

    public TextToSpeechService(
        DeepgramTtsService deepgramTts,
        PiperTtsService piperTts,
        EdgeTtsService edgeTts,
        ElevenLabsTtsService elevenLabsTts,
        SettingsService settingsService)
    {
        _deepgramTts = deepgramTts ?? throw new ArgumentNullException(nameof(deepgramTts));
        _piperTts = piperTts ?? throw new ArgumentNullException(nameof(piperTts));
        _edgeTts = edgeTts ?? throw new ArgumentNullException(nameof(edgeTts));
        _elevenLabsTts = elevenLabsTts ?? throw new ArgumentNullException(nameof(elevenLabsTts));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        _deepgramTts.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(s, e);
        _piperTts.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(s, e);
        _edgeTts.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(s, e);
        _elevenLabsTts.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(s, e);
    }

    public async Task<ITextToSpeechService> GetActiveProviderAsync()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        if (settings.PreferredTtsProvider.Equals("ElevenLabs", StringComparison.OrdinalIgnoreCase))
            return _elevenLabsTts;

        if (settings.PreferredTtsProvider.Equals("EdgeTts", StringComparison.OrdinalIgnoreCase))
            return _edgeTts;

        return settings.PreferredTtsProvider.Equals("Piper", StringComparison.OrdinalIgnoreCase)
            ? _piperTts
            : _deepgramTts;
    }

    public async Task<bool> IsLanguageSupportedAsync(string languageCode)
    {
        var active = await GetActiveProviderAsync();
        return await active.IsLanguageSupportedAsync(languageCode);
    }

    public async Task<byte[]> SynthesizeAsync(
        string text,
        string languageCode,
        TtsOptions options,
        CancellationToken cancellationToken = default)
    {
        var active = await GetActiveProviderAsync();
        return await active.SynthesizeAsync(text, languageCode, options, cancellationToken);
    }
}