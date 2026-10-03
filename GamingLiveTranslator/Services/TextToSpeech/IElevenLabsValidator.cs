using GamingLiveTranslator.Services.Speech;

namespace GamingLiveTranslator.Services.TextToSpeech;

/// <summary>
/// Interface for validating ElevenLabs API credentials.
/// </summary>
public interface IElevenLabsValidator
{
    /// <summary>
    /// Validates the specified ElevenLabs API key against the subscription endpoint.
    /// Never logs or persists the key.
    /// </summary>
    Task<ApiValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
}
