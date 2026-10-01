using GamingLiveTranslator.Models;

namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Result of a machine translation operation.
/// </summary>
public class TranslationResult
{
    public bool IsSuccess { get; set; }
    public string TranslatedText { get; set; } = string.Empty;
    public string? DetectedSourceLanguage { get; set; }
    public string? ErrorMessage { get; set; }
    public TranslationFailureType FailureType { get; set; } = TranslationFailureType.None;
    public int TranslatedCharacters { get; set; }

    public static TranslationResult Success(string translatedText, string? detectedLanguage = null, int charCount = 0) =>
        new() { IsSuccess = true, TranslatedText = translatedText, DetectedSourceLanguage = detectedLanguage, TranslatedCharacters = charCount };

    public static TranslationResult Fail(string errorMessage, TranslationFailureType failureType = TranslationFailureType.None) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, FailureType = failureType };
}

/// <summary>
/// Core machine translation service interface.
/// </summary>
public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, string? apiKey = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Language>> GetSupportedLanguagesAsync();
}
