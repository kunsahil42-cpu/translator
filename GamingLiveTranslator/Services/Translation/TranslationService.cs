using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Translation service implementing Google Cloud Translation API v2 (Basic).
/// Uses simple API key query authentication without requiring Service Accounts or OAuth2 tokens.
/// </summary>
public class TranslationService : ITranslationService
{
    private const string BaseApiUrl = "https://translation.googleapis.com/language/translate/v2";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly ISecureCredentialStore _credentialStore;

    public TranslationService(ISecureCredentialStore? credentialStore = null)
    {
        _credentialStore = credentialStore ?? new SecureCredentialStore();
    }

    public Task<IReadOnlyList<Language>> GetSupportedLanguagesAsync()
    {
        return Task.FromResult(Language.GetInitialLanguages(includeAutoDetect: false));
    }

    public virtual async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TranslationResult.Success(string.Empty);

        apiKey ??= await _credentialStore.GetApiKeyAsync("GoogleTranslate");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return TranslationResult.Fail(
                "Google Translation API key is missing. Please configure it in Settings.",
                TranslationFailureType.AuthenticationOrConfigurationError);
        }

        try
        {
            var url = $"{BaseApiUrl}?key={HttpUtility.UrlEncode(apiKey.Trim())}";

            // Build request payload
            var payload = new Dictionary<string, object>
            {
                ["q"] = text,
                ["target"] = targetLanguage,
                ["format"] = "text"
            };

            if (!string.IsNullOrWhiteSpace(sourceLanguage) && !sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                payload["source"] = sourceLanguage;
            }

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, jsonContent, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseJson);

                var data = doc.RootElement.GetProperty("data");
                var translations = data.GetProperty("translations");

                if (translations.GetArrayLength() > 0)
                {
                    var firstTranslation = translations[0];
                    var translatedText = firstTranslation.GetProperty("translatedText").GetString() ?? string.Empty;

                    // Unescape any HTML entities returned by Google Translate
                    translatedText = WebUtility.HtmlDecode(translatedText);

                    string? detectedLang = null;
                    if (firstTranslation.TryGetProperty("detectedSourceLanguage", out var dLang))
                    {
                        detectedLang = dLang.GetString();
                    }

                    return TranslationResult.Success(translatedText, detectedLang, text.Length);
                }

                return TranslationResult.Fail("No translation returned.", TranslationFailureType.BadRequestOrClientError);
            }

            var errJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == (HttpStatusCode)429)
            {
                return TranslationResult.Fail("Rate limit exceeded on Google Cloud Translation.", TranslationFailureType.RateLimitOrQuotaExceeded);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Forbidden)
            {
                if (errJson.Contains("SERVICE_DISABLED", StringComparison.OrdinalIgnoreCase))
                {
                    return TranslationResult.Fail("Cloud Translation API is disabled in your Google Cloud project.", TranslationFailureType.AuthenticationOrConfigurationError);
                }

                if (errJson.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
                    errJson.Contains("keyInvalid", StringComparison.OrdinalIgnoreCase))
                {
                    return TranslationResult.Fail("Invalid Google Translation API key.", TranslationFailureType.AuthenticationOrConfigurationError);
                }

                if (errJson.Contains("RATE_LIMIT_EXCEEDED", StringComparison.OrdinalIgnoreCase) ||
                    errJson.Contains("userRateLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
                    errJson.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
                    errJson.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase) ||
                    errJson.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
                    errJson.Contains("billing", StringComparison.OrdinalIgnoreCase))
                {
                    return TranslationResult.Fail("Google Cloud Translation quota or rate limit exceeded.", TranslationFailureType.RateLimitOrQuotaExceeded);
                }

                return TranslationResult.Fail("Google Translation API key rejected or forbidden.", TranslationFailureType.AuthenticationOrConfigurationError);
            }

            if ((int)response.StatusCode >= 500)
            {
                return TranslationResult.Fail($"Google Cloud Translation server error (HTTP {(int)response.StatusCode}).", TranslationFailureType.TransientServerError);
            }

            return TranslationResult.Fail($"Translation API returned HTTP {(int)response.StatusCode}.", TranslationFailureType.TransientServerError);
        }
        catch (TaskCanceledException)
        {
            return TranslationResult.Fail("Translation request timed out.", TranslationFailureType.TransientServerError);
        }
        catch (HttpRequestException ex)
        {
            return TranslationResult.Fail($"Network error during translation: {ex.Message}", TranslationFailureType.TransientServerError);
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected exception in TranslationService.", ex);
            return TranslationResult.Fail("An unexpected error occurred during translation.", TranslationFailureType.TransientServerError);
        }
    }
}
