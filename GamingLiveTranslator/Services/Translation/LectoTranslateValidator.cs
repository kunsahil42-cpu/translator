using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GamingLiveTranslator.Services.Speech;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Dedicated validator for Lecto Translation API credentials via RapidAPI.
/// Makes a lightweight 1-word translation probe to verify authentication and subscription status.
/// </summary>
public class LectoTranslateValidator : ILectoTranslateValidator
{
    private const string ApiEndpoint = "https://lecto-translation.p.rapidapi.com/v1/translate/text";
    private const string ApiHost = "lecto-translation.p.rapidapi.com";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<ApiValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiValidationResult(false, "RapidAPI key cannot be empty or whitespace.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
            request.Headers.Add("x-rapidapi-key", apiKey.Trim());
            request.Headers.Add("x-rapidapi-host", ApiHost);

            var payload = new Dictionary<string, object>
            {
                ["texts"] = new[] { "Hello" },
                ["to"] = new[] { "es" },
                ["from"] = "en"
            };

            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new ApiValidationResult(true, "Connected successfully to Lecto.");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ApiValidationResult(false, "Invalid RapidAPI key. RapidAPI rejected the key.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                if (responseBody.Contains("not subscribed", StringComparison.OrdinalIgnoreCase))
                {
                    return new ApiValidationResult(false, "Your RapidAPI account is not subscribed to 'Lecto Translation'. Subscribe on RapidAPI.");
                }

                if (responseBody.Contains("inactive", StringComparison.OrdinalIgnoreCase))
                {
                    return new ApiValidationResult(false, "Your Lecto subscription is inactive on RapidAPI.");
                }

                return new ApiValidationResult(false, "Access forbidden. Check your RapidAPI subscription for Lecto.");
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                return new ApiValidationResult(true, "Key is valid, but rate limit / quota is currently reached on Lecto.");
            }

            return new ApiValidationResult(false, $"Lecto returned HTTP {(int)response.StatusCode}: {responseBody}");
        }
        catch (TaskCanceledException)
        {
            return new ApiValidationResult(false, "Connection timed out. Lecto RapidAPI endpoint was unreachable.");
        }
        catch (HttpRequestException ex)
        {
            return new ApiValidationResult(false, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected error validating Lecto API key.", ex);
            return new ApiValidationResult(false, "An unexpected error occurred during validation.");
        }
    }
}
