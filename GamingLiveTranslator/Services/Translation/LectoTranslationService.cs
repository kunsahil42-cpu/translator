using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Machine translation service implementing Lecto Translation API via RapidAPI.
/// Supports neural translation across 90+ languages with fast single-utterance execution.
/// </summary>
public class LectoTranslationService : ITranslationService
{
    private const string ApiEndpoint = "https://lecto-translation.p.rapidapi.com/v1/translate/text";
    private const string ApiHost = "lecto-translation.p.rapidapi.com";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly ISecureCredentialStore _credentialStore;

    public LectoTranslationService(ISecureCredentialStore? credentialStore = null)
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
            return TranslationResult.Success(string.Empty, null, 0);

        apiKey ??= await _credentialStore.GetApiKeyAsync("Lecto");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return TranslationResult.Fail(
                "Lecto RapidAPI key is missing. Please configure it in Settings.",
                TranslationFailureType.AuthenticationOrConfigurationError);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
            request.Headers.Add("x-rapidapi-key", apiKey.Trim());
            request.Headers.Add("x-rapidapi-host", ApiHost);

            // Construct JSON request body
            var payload = new Dictionary<string, object>
            {
                ["texts"] = new[] { text },
                ["to"] = new[] { targetLanguage }
            };

            if (!string.IsNullOrWhiteSpace(sourceLanguage) && !sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                payload["from"] = sourceLanguage;
            }

            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseJson);

                string? detectedSource = null;
                if (doc.RootElement.TryGetProperty("from", out var fromProp))
                {
                    detectedSource = fromProp.GetString();
                }

                int charCount = text.Length;
                if (doc.RootElement.TryGetProperty("translated_characters", out var charProp) && charProp.TryGetInt32(out var parsedChars))
                {
                    charCount = parsedChars;
                }

                if (doc.RootElement.TryGetProperty("translations", out var translationsProp) &&
                    translationsProp.ValueKind == JsonValueKind.Array &&
                    translationsProp.GetArrayLength() > 0)
                {
                    var firstItem = translationsProp[0];
                    if (firstItem.TryGetProperty("translated", out var translatedArray) &&
                        translatedArray.ValueKind == JsonValueKind.Array &&
                        translatedArray.GetArrayLength() > 0)
                    {
                        var translatedText = translatedArray[0].GetString() ?? string.Empty;
                        return TranslationResult.Success(translatedText, detectedSource, charCount);
                    }
                }

                return TranslationResult.Fail("No translation array returned in Lecto response.", TranslationFailureType.BadRequestOrClientError);
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            // HTTP 429: Too Many Requests (Rate limit per second or monthly quota cap on RapidAPI)
            if (response.StatusCode == (HttpStatusCode)429)
            {
                var message = ExtractErrorMessage(responseBody) ?? "Too many requests / quota exceeded on Lecto RapidAPI.";
                return TranslationResult.Fail($"Lecto rate limit or quota exceeded: {message}", TranslationFailureType.RateLimitOrQuotaExceeded);
            }

            // HTTP 401: Unauthorized (Invalid or malformed RapidAPI key)
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var message = ExtractErrorMessage(responseBody) ?? "Invalid RapidAPI key.";
                return TranslationResult.Fail($"Lecto authentication failed (HTTP 401): {message}", TranslationFailureType.AuthenticationOrConfigurationError);
            }

            // HTTP 403: Forbidden (Check body to distinguish Auth/Subscription from Quota)
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var message = ExtractErrorMessage(responseBody) ?? responseBody;
                if (message.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("exceeded", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("limit", StringComparison.OrdinalIgnoreCase))
                {
                    return TranslationResult.Fail($"Lecto quota limit reached (HTTP 403): {message}", TranslationFailureType.RateLimitOrQuotaExceeded);
                }

                return TranslationResult.Fail(
                    $"Lecto access forbidden (HTTP 403): {message}. Verify subscription on RapidAPI.",
                    TranslationFailureType.AuthenticationOrConfigurationError);
            }

            // HTTP 400: Bad Request (Invalid parameters, unsupported language pair, text length limits)
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var message = ExtractErrorMessage(responseBody) ?? "Invalid request parameters.";
                return TranslationResult.Fail($"Lecto Bad Request (HTTP 400): {message}", TranslationFailureType.BadRequestOrClientError);
            }

            // HTTP 5xx: Server Outage / Gateway error
            if ((int)response.StatusCode >= 500)
            {
                return TranslationResult.Fail(
                    $"Lecto server error (HTTP {(int)response.StatusCode}).",
                    TranslationFailureType.TransientServerError);
            }

            return TranslationResult.Fail(
                $"Lecto API returned HTTP {(int)response.StatusCode}: {responseBody}",
                TranslationFailureType.TransientServerError);
        }
        catch (TaskCanceledException)
        {
            return TranslationResult.Fail("Lecto translation request timed out.", TranslationFailureType.TransientServerError);
        }
        catch (HttpRequestException ex)
        {
            return TranslationResult.Fail($"Network error contacting Lecto API: {ex.Message}", TranslationFailureType.TransientServerError);
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected exception in LectoTranslationService.", ex);
            return TranslationResult.Fail("An unexpected error occurred during Lecto translation.", TranslationFailureType.TransientServerError);
        }
    }

    private static string? ExtractErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            // RapidAPI gateway shape: {"message": "..."}
            if (doc.RootElement.TryGetProperty("message", out var msgProp))
            {
                return msgProp.GetString();
            }

            // Lecto app error shape: {"status": 400, "details": {"message": "..."}}
            if (doc.RootElement.TryGetProperty("details", out var detailsProp))
            {
                if (detailsProp.TryGetProperty("message", out var detMsg))
                {
                    return detMsg.GetString();
                }
                if (detailsProp.TryGetProperty("text", out var detText))
                {
                    return detText.GetString();
                }
            }
        }
        catch
        {
            // Non-JSON response
        }

        return responseBody;
    }
}
