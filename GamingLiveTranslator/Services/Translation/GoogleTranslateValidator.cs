using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web;
using GamingLiveTranslator.Services.Speech;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Dedicated validator for Google Cloud Translation API credentials.
/// Makes a lightweight translation test call and checks for specific setup errors (e.g. API not enabled).
/// </summary>
public class GoogleTranslateValidator : IGoogleTranslateValidator
{
    private const string BaseApiUrl = "https://translation.googleapis.com/language/translate/v2";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<ApiValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiValidationResult(false, "API key cannot be empty or whitespace.");
        }

        try
        {
            var url = $"{BaseApiUrl}?key={HttpUtility.UrlEncode(apiKey.Trim())}";
            var payload = new Dictionary<string, string>
            {
                ["q"] = "Hello",
                ["target"] = "es",
                ["source"] = "en",
                ["format"] = "text"
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, jsonContent, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new ApiValidationResult(true, "Connected successfully.");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.BadRequest)
            {
                if (responseBody.Contains("SERVICE_DISABLED", StringComparison.OrdinalIgnoreCase) ||
                    responseBody.Contains("has not been used in project", StringComparison.OrdinalIgnoreCase) ||
                    responseBody.Contains("it is disabled", StringComparison.OrdinalIgnoreCase))
                {
                    return new ApiValidationResult(false, "Cloud Translation API is not enabled in your Google Cloud project. Enable it in Google Cloud Console.");
                }

                if (responseBody.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
                    responseBody.Contains("keyInvalid", StringComparison.OrdinalIgnoreCase))
                {
                    return new ApiValidationResult(false, "Invalid API key. Google rejected the key.");
                }

                if (responseBody.Contains("BILLING_DISABLED", StringComparison.OrdinalIgnoreCase) ||
                    responseBody.Contains("billing", StringComparison.OrdinalIgnoreCase))
                {
                    return new ApiValidationResult(false, "Google Cloud project billing is not enabled. Check project billing status.");
                }

                return new ApiValidationResult(false, "Invalid Google Translation API key or permission denied.");
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                return new ApiValidationResult(false, "Rate limit / quota exceeded on Google Cloud Translation.");
            }

            return new ApiValidationResult(false, $"Google returned error (HTTP {(int)response.StatusCode}).");
        }
        catch (TaskCanceledException)
        {
            return new ApiValidationResult(false, "Connection timed out. Google Translation was unreachable.");
        }
        catch (HttpRequestException ex)
        {
            return new ApiValidationResult(false, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected error validating Google Translation API key.", ex);
            return new ApiValidationResult(false, "An unexpected error occurred during validation.");
        }
    }
}
