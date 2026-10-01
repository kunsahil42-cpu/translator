using GamingLiveTranslator.Models;

namespace GamingLiveTranslator.Services.TextToSpeech;

/// <summary>
/// Interface for text-to-speech synthesis providers.
/// Standardizes synthesis output to 16-bit PCM WAV audio bytes.
/// </summary>
public interface ITextToSpeechService
{
    /// <summary>
    /// Friendly name of the TTS provider (e.g. "Deepgram", "Piper").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Synthesizes text in the given target language to audio bytes (standard 16-bit PCM WAV).
    /// </summary>
    Task<byte[]> SynthesizeAsync(string text, string languageCode, TtsOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the provider supports speech synthesis for the specified ISO language code.
    /// </summary>
    Task<bool> IsLanguageSupportedAsync(string languageCode);

    /// <summary>
    /// Raised when a synthesis or provider communication error occurs.
    /// </summary>
    event EventHandler<TtsErrorEventArgs>? ErrorOccurred;
}
