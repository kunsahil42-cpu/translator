namespace GamingLiveTranslator.Models;

/// <summary>
/// Represents a configured translation provider account or engine in the fallback pool.
/// Sensitive credentials (API keys) are stored separately via ISecureCredentialStore keyed by Id.
/// </summary>
public class TranslationProviderEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Type of provider: "GoogleTranslate", "Lecto", or "ArgosTranslate".
    /// </summary>
    public string ProviderType { get; set; } = "GoogleTranslate";

    /// <summary>
    /// User-editable label (e.g. "My Google Key", "Backup Lecto", "Offline Engine").
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Order in which the provider pool evaluates entries (1 is tried first).
    /// </summary>
    public int Priority { get; set; } = 1;

    /// <summary>
    /// Whether this entry is active for evaluation.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional user-defined soft limit in characters per period (e.g., 500,000 chars).
    /// When estimated usage reaches this limit, the pool proactively skips to the next entry.
    /// </summary>
    public long? SoftUsageLimitChars { get; set; }

    /// <summary>
    /// Self-tracked estimated character count processed in the current period.
    /// </summary>
    public long CurrentPeriodUsageChars { get; set; }

    /// <summary>
    /// UTC timestamp marking the start of the current tracking period (e.g. monthly).
    /// </summary>
    public DateTime? UsagePeriodStartDate { get; set; } = DateTime.UtcNow;
}
