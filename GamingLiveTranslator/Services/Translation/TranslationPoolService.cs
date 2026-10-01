using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Orchestrates translation requests across a prioritized pool of genuine translation provider accounts.
/// Evaluates self-tracked soft usage quotas and automatically falls back on rate limits (HTTP 429),
/// provider quota limits, or server outages, while surfacing real configuration and authentication errors.
/// </summary>
public class TranslationPoolService : ITranslationPoolService
{
    private readonly SettingsService _settingsService;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly TranslationService _googleService;
    private readonly LectoTranslationService _lectoService;
    private readonly LocalArgosTranslationService? _argosService;

    private readonly SemaphoreSlim _poolLock = new(1, 1);

    public event Action<string, string?>? PoolStateChanged;

    public string? CurrentActiveProviderLabel { get; private set; }
    public string? CurrentFallbackNotice { get; private set; }

    public TranslationPoolService(
        SettingsService settingsService,
        ISecureCredentialStore credentialStore,
        TranslationService googleService,
        LectoTranslationService lectoService,
        LocalArgosTranslationService? argosService = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _googleService = googleService ?? throw new ArgumentNullException(nameof(googleService));
        _lectoService = lectoService ?? throw new ArgumentNullException(nameof(lectoService));
        _argosService = argosService;
    }

    public async Task<List<TranslationProviderEntry>> GetPoolEntriesAsync()
    {
        await _poolLock.WaitAsync();
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            await EnsurePoolInitializedAsync(settings);
            return settings.TranslationProviderPool.OrderBy(e => e.Priority).ToList();
        }
        finally
        {
            _poolLock.Release();
        }
    }

    public async Task SavePoolEntriesAsync(IEnumerable<TranslationProviderEntry> entries)
    {
        await _poolLock.WaitAsync();
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            settings.TranslationProviderPool = entries.OrderBy(e => e.Priority).ToList();
            await _settingsService.SaveSettingsAsync(settings);
            Logger.Info($"Saved {settings.TranslationProviderPool.Count} translation pool entries.");
        }
        finally
        {
            _poolLock.Release();
        }
    }

    public async Task ResetUsageCounterAsync(string entryId)
    {
        await _poolLock.WaitAsync();
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var target = settings.TranslationProviderPool.FirstOrDefault(e => e.Id == entryId);
            if (target != null)
            {
                target.CurrentPeriodUsageChars = 0;
                target.UsagePeriodStartDate = DateTime.UtcNow;
                await _settingsService.SaveSettingsAsync(settings);
                Logger.Info($"Reset estimated usage counter for translation provider '{target.Label}'.");
            }
        }
        finally
        {
            _poolLock.Release();
        }
    }

    public async Task<TranslationPoolResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationPoolResult(true, string.Empty, null, null, CurrentActiveProviderLabel, null, null);
        }

        List<TranslationProviderEntry> pool;
        ApiSettings settings;

        await _poolLock.WaitAsync(cancellationToken);
        try
        {
            settings = await _settingsService.LoadSettingsAsync();
            await EnsurePoolInitializedAsync(settings);
            pool = settings.TranslationProviderPool.Where(e => e.IsEnabled).OrderBy(e => e.Priority).ToList();
        }
        finally
        {
            _poolLock.Release();
        }

        if (pool.Count == 0)
        {
            return new TranslationPoolResult(
                false,
                string.Empty,
                null,
                "No enabled translation provider entries in the pool. Please enable at least one provider in Settings.",
                null,
                null,
                null);
        }

        var failureReasons = new List<string>();
        string? firstAttemptedLabel = null;
        string? fallbackReason = null;

        for (int i = 0; i < pool.Count; i++)
        {
            var entry = pool[i];

            // Auto-check monthly rollover
            CheckMonthlyRollover(entry);

            // Proactive Soft Limit Check
            if (entry.SoftUsageLimitChars.HasValue && entry.CurrentPeriodUsageChars >= entry.SoftUsageLimitChars.Value)
            {
                var note = $"Skipped '{entry.Label}': estimated soft limit reached (~{entry.CurrentPeriodUsageChars:N0} / {entry.SoftUsageLimitChars.Value:N0} chars).";
                Logger.Info(note);
                failureReasons.Add(note);
                if (firstAttemptedLabel == null)
                {
                    firstAttemptedLabel = entry.Label;
                    fallbackReason = "estimated quota limit reached";
                }
                continue;
            }

            firstAttemptedLabel ??= entry.Label;

            // Resolve secret key from DPAPI store
            string? apiKey = await ResolveApiKeyForEntryAsync(entry);

            // If a cloud provider requires an API key and none is configured, skip to next provider
            if (string.IsNullOrWhiteSpace(apiKey) && entry.ProviderType != "ArgosTranslate")
            {
                var note = $"Skipped '{entry.Label}': API key is not configured.";
                Logger.Info(note);
                failureReasons.Add(note);
                if (firstAttemptedLabel == null)
                {
                    firstAttemptedLabel = entry.Label;
                    fallbackReason = "API key not configured";
                }
                continue;
            }

            var (service, serviceKey) = ResolveService(entry, apiKey);
            if (service == null)
            {
                var reason = $"Provider type '{entry.ProviderType}' for '{entry.Label}' is not supported or unavailable.";
                failureReasons.Add(reason);
                continue;
            }

            // Attempt translation
            var result = await service.TranslateAsync(text, sourceLanguage, targetLanguage, serviceKey, cancellationToken);

            if (result.IsSuccess)
            {
                // Usage counter increment
                int charsUsed = result.TranslatedCharacters > 0 ? result.TranslatedCharacters : text.Length;
                entry.CurrentPeriodUsageChars += charsUsed;
                _ = PersistUpdatedUsageAsync(entry.Id, entry.CurrentPeriodUsageChars);

                // Fallback detection
                string? notice = null;
                if (i > 0 || fallbackReason != null)
                {
                    var reasonDesc = fallbackReason ?? "fallback from prior provider";
                    notice = $"fallback from {firstAttemptedLabel} — {reasonDesc}";
                }

                CurrentActiveProviderLabel = entry.Label;
                CurrentFallbackNotice = notice;

                PoolStateChanged?.Invoke(entry.Label, notice);

                return new TranslationPoolResult(
                    true,
                    result.TranslatedText,
                    result.DetectedSourceLanguage,
                    null,
                    entry.Label,
                    entry.ProviderType,
                    notice);
            }

            // Failure handling based on verified failure classification
            switch (result.FailureType)
            {
                case TranslationFailureType.AuthenticationOrConfigurationError:
                    if (i < pool.Count - 1)
                    {
                        fallbackReason ??= $"{entry.Label} authentication error";
                        failureReasons.Add($"'{entry.Label}' auth error: {result.ErrorMessage}");
                        Logger.Warn($"Authentication failure on '{entry.Label}': {result.ErrorMessage}. Falling back to next provider in pool.");
                        break;
                    }
                    return new TranslationPoolResult(
                        false,
                        string.Empty,
                        null,
                        $"[{entry.Label} Auth Error]: {result.ErrorMessage}",
                        entry.Label,
                        entry.ProviderType,
                        null);

                case TranslationFailureType.BadRequestOrClientError:
                    // Bad input or unsupported operation: DO NOT fall back.
                    Logger.Warn($"Bad request on '{entry.Label}': {result.ErrorMessage}");
                    return new TranslationPoolResult(
                        false,
                        string.Empty,
                        null,
                        $"[{entry.Label} Error]: {result.ErrorMessage}",
                        entry.Label,
                        entry.ProviderType,
                        null);

                case TranslationFailureType.RateLimitOrQuotaExceeded:
                    fallbackReason ??= "rate limit / quota reached";
                    failureReasons.Add($"'{entry.Label}' rate/quota reached: {result.ErrorMessage}");
                    Logger.Info($"Provider '{entry.Label}' hit rate/quota limit. Falling through to next pool entry.");
                    break;

                case TranslationFailureType.TransientServerError:
                    fallbackReason ??= "server outage / timeout";
                    failureReasons.Add($"'{entry.Label}' server error: {result.ErrorMessage}");
                    Logger.Warn($"Provider '{entry.Label}' encountered transient server error. Trying next entry.");
                    break;

                case TranslationFailureType.EngineUnavailable:
                    fallbackReason ??= "offline engine unavailable";
                    failureReasons.Add($"'{entry.Label}' offline engine unavailable: {result.ErrorMessage}");
                    Logger.Warn($"Provider '{entry.Label}' offline engine unavailable. Trying next entry.");
                    break;

                default:
                    failureReasons.Add($"'{entry.Label}' failed: {result.ErrorMessage}");
                    break;
            }
        }

        // All configured enabled providers failed
        var consolidated = string.Join("; ", failureReasons);
        return new TranslationPoolResult(
            false,
            string.Empty,
            null,
            $"All translation providers in pool exhausted or failed: {consolidated}",
            null,
            null,
            null);
    }

    private (ITranslationService? service, string? apiKey) ResolveService(TranslationProviderEntry entry, string? apiKey)
    {
        return entry.ProviderType switch
        {
            "GoogleTranslate" => (_googleService, apiKey),
            "Lecto" => (_lectoService, apiKey),
            "ArgosTranslate" => (_argosService, null),
            _ => (null, null)
        };
    }

    private async Task<string?> ResolveApiKeyForEntryAsync(TranslationProviderEntry entry)
    {
        if (entry.ProviderType.Equals("ArgosTranslate", StringComparison.OrdinalIgnoreCase))
            return null;

        // Try entry-specific GUID key first
        var key = await _credentialStore.GetApiKeyAsync(entry.Id);
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        // Fallback migration check for legacy provider-type keys
        if (entry.ProviderType.Equals("GoogleTranslate", StringComparison.OrdinalIgnoreCase))
        {
            var legacyKey = await _credentialStore.GetApiKeyAsync("GoogleTranslate");
            if (!string.IsNullOrWhiteSpace(legacyKey))
            {
                // Auto-migrate to entry Id
                await _credentialStore.SaveApiKeyAsync(entry.Id, legacyKey);
                return legacyKey;
            }
        }
        else if (entry.ProviderType.Equals("Lecto", StringComparison.OrdinalIgnoreCase))
        {
            var legacyKey = await _credentialStore.GetApiKeyAsync("Lecto");
            if (!string.IsNullOrWhiteSpace(legacyKey))
            {
                await _credentialStore.SaveApiKeyAsync(entry.Id, legacyKey);
                return legacyKey;
            }
        }

        return null;
    }

    private async Task EnsurePoolInitializedAsync(ApiSettings settings)
    {
        if (settings.TranslationProviderPool == null)
        {
            settings.TranslationProviderPool = new List<TranslationProviderEntry>();
        }

        if (settings.TranslationProviderPool.Count == 0)
        {
            Logger.Info("Initializing default Translation Provider Pool with backward-compatible entries.");

            // 1. Google Translate entry
            var googleId = Guid.NewGuid().ToString();
            var googleKey = await _credentialStore.GetApiKeyAsync("GoogleTranslate");
            if (!string.IsNullOrWhiteSpace(googleKey))
            {
                await _credentialStore.SaveApiKeyAsync(googleId, googleKey);
            }

            var googleEntry = new TranslationProviderEntry
            {
                Id = googleId,
                ProviderType = "GoogleTranslate",
                Label = "Google Cloud Translation",
                Priority = 1,
                IsEnabled = true,
                SoftUsageLimitChars = 500000, // standard free tier monthly soft cap
                CurrentPeriodUsageChars = 0,
                UsagePeriodStartDate = DateTime.UtcNow
            };

            // 2. Argos Translate entry
            var argosEntry = new TranslationProviderEntry
            {
                Id = Guid.NewGuid().ToString(),
                ProviderType = "ArgosTranslate",
                Label = "Argos Translate (Offline Engine)",
                Priority = 2,
                IsEnabled = true,
                SoftUsageLimitChars = null,
                CurrentPeriodUsageChars = 0,
                UsagePeriodStartDate = DateTime.UtcNow
            };

            settings.TranslationProviderPool.Add(googleEntry);
            settings.TranslationProviderPool.Add(argosEntry);

            await _settingsService.SaveSettingsAsync(settings);
        }
    }

    private static void CheckMonthlyRollover(TranslationProviderEntry entry)
    {
        if (entry.UsagePeriodStartDate.HasValue)
        {
            var elapsed = DateTime.UtcNow - entry.UsagePeriodStartDate.Value;
            if (elapsed.TotalDays >= 30.0)
            {
                entry.CurrentPeriodUsageChars = 0;
                entry.UsagePeriodStartDate = DateTime.UtcNow;
            }
        }
        else
        {
            entry.UsagePeriodStartDate = DateTime.UtcNow;
        }
    }

    private async Task PersistUpdatedUsageAsync(string entryId, long newUsage)
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var match = settings.TranslationProviderPool.FirstOrDefault(e => e.Id == entryId);
            if (match != null)
            {
                match.CurrentPeriodUsageChars = newUsage;
                await _settingsService.SaveSettingsAsync(settings);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to persist updated usage for entry {entryId}: {ex.Message}");
        }
    }
}
