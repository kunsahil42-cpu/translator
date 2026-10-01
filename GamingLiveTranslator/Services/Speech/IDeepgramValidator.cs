namespace GamingLiveTranslator.Services.Speech;

/// <summary>
/// Result of an API key validation attempt.
/// </summary>
public record ApiValidationResult(bool IsSuccess, string Message);

/// <summary>
/// Interface for validating Deepgram API credentials against real service endpoints.
/// </summary>
public interface IDeepgramValidator
{
    Task<ApiValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
}
