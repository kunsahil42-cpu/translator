using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Speech;

/// <summary>
/// Validates Deepgram credentials using the lightweight authenticated endpoint:
/// GET https://api.deepgram.com/v1/projects
/// </summary>
public class DeepgramValidator : IDeepgramValidator
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private const string DeepgramProjectsEndpoint = "https://api.deepgram.com/v1/projects";

    public async Task<ApiValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiValidationResult(false, "API key cannot be empty or whitespace.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DeepgramProjectsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", apiKey.Trim());
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GamingLiveTranslator", "1.0"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new ApiValidationResult(true, "Connected successfully.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new ApiValidationResult(false, "Invalid API key. Deepgram rejected authentication.");
            }

            return new ApiValidationResult(false, $"Deepgram returned error (HTTP {(int)response.StatusCode}: {response.ReasonPhrase}).");
        }
        catch (TaskCanceledException)
        {
            return new ApiValidationResult(false, "Connection timed out. Deepgram was unreachable.");
        }
        catch (HttpRequestException ex)
        {
            return new ApiValidationResult(false, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected error occurred while testing Deepgram connection.", ex);
            return new ApiValidationResult(false, "An unexpected error occurred during connection test.");
        }
    }
}
