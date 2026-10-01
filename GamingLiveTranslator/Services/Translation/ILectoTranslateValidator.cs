using GamingLiveTranslator.Services.Speech;

namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Interface for validating Lecto Translation API credentials on RapidAPI.
/// </summary>
public interface ILectoTranslateValidator
{
    Task<ApiValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
}
