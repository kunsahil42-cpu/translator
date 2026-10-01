using GamingLiveTranslator.Models;

namespace GamingLiveTranslator.Services.Translation;

public record TranslationPoolResult(
    bool IsSuccess,
    string TranslatedText,
    string? DetectedSourceLanguage,
    string? ErrorMessage,
    string? ActiveProviderLabel,
    string? ActiveProviderType,
    string? FallbackNotice
);

public interface ITranslationPoolService
{
    event Action<string, string?>? PoolStateChanged;

    Task<TranslationPoolResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default);

    Task<List<TranslationProviderEntry>> GetPoolEntriesAsync();
    Task SavePoolEntriesAsync(IEnumerable<TranslationProviderEntry> entries);
    Task ResetUsageCounterAsync(string entryId);

    string? CurrentActiveProviderLabel { get; }
    string? CurrentFallbackNotice { get; }
}
