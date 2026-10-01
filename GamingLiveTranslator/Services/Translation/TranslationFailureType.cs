namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Categorizes translation failures to govern pool fallback behavior vs hard error surfacing.
/// </summary>
public enum TranslationFailureType
{
    None = 0,

    /// <summary>
    /// Transient rate limit (HTTP 429) or billing quota exhaustion.
    /// Action: Fall back to next provider in pool.
    /// </summary>
    RateLimitOrQuotaExceeded,

    /// <summary>
    /// Upstream cloud 5xx error or connection timeout.
    /// Action: Fall back to next provider in pool.
    /// </summary>
    TransientServerError,

    /// <summary>
    /// Local offline engine (Argos) is stopped or requested language pair model is uninstalled.
    /// Action: Fall back to next provider in pool.
    /// </summary>
    EngineUnavailable,

    /// <summary>
    /// Authentication failure (HTTP 401, invalid API key, inactive subscription).
    /// Action: DO NOT fall back. Immediately surface error to avoid masking configuration errors.
    /// </summary>
    AuthenticationOrConfigurationError,

    /// <summary>
    /// Malformed request or client parameter error (HTTP 400).
    /// Action: DO NOT fall back. Immediately surface error.
    /// </summary>
    BadRequestOrClientError
}
