using GamingLiveTranslator.Services.Speech;

namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Interface for validating Google Cloud Translation API credentials.
/// </summary>
public interface IGoogleTranslateValidator
{
    Task<ApiValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
}
