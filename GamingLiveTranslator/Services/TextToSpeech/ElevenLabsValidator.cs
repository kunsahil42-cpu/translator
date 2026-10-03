using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GamingLiveTranslator.Services.Speech;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.TextToSpeech;

/// <summary>
/// Validates ElevenLabs API credentials using the lightweight authenticated endpoint:
/// GET https://api.elevenlabs.io/v1/user/subscription
/// Retrieves tier and credit usage information without consuming TTS character credits.
/// </summary>
public class ElevenLabsValidator : IElevenLabsValidator
{
    private const string SubscriptionEndpoint = "https://api.elevenlabs.io/v1/user/subscription";
    private const string VoicesEndpoint = "https://api.elevenlabs.io/v1/voices";
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Strips enclosing quotes, whitespace, accidental prefixes (Bearer, xi-api-key:),
    /// and hidden invisible/zero-width Unicode characters.
    /// </summary>
    public static string SanitizeKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return string.Empty;

        var key = rawKey.Trim().Trim('"', '\'', '`');
        if (key.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            key = key.Substring(7).Trim();
        }
        else if (key.StartsWith("xi-api-key:", StringComparison.OrdinalIgnoreCase))
        {
            key = key.Substring(11).Trim();
        }

        // Strip non-printable and zero-width characters (e.g. \u200B zero-width space, \uFEFF BOM, \u00A0 non-breaking space)
        key = new string(key.Where(c => !char.IsControl(c) && c != '\u200B' && c != '\u200C' && c != '\u200D' && c != '\uFEFF').ToArray());
        return key.Trim();
    }

    public async Task<ApiValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        var cleanKey = SanitizeKey(apiKey);
        if (string.IsNullOrWhiteSpace(cleanKey))
        {
            return new ApiValidationResult(false, "API key cannot be empty or whitespace.");
        }

        if (cleanKey.StartsWith("agent_", StringComparison.OrdinalIgnoreCase))
        {
            return new ApiValidationResult(false, "This appears to be an Agent ID ('agent_...'). ElevenLabs TTS requires an API Key (starts with 'sk_'), found under elevenlabs.io -> Settings -> API Keys.");
        }

        try
        {
            // 1. Try Subscription endpoint (retrieves tier & credit usage)
            using var subRequest = new HttpRequestMessage(HttpMethod.Get, SubscriptionEndpoint);
            subRequest.Headers.Add("xi-api-key", cleanKey);

            using var subResponse = await _httpClient.SendAsync(subRequest, cancellationToken);

            if (subResponse.IsSuccessStatusCode)
            {
                var json = await subResponse.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tier = root.TryGetProperty("tier", out var tierProp) ? tierProp.GetString() ?? "Free" : "Unknown";
                var charCount = root.TryGetProperty("character_count", out var countProp) ? countProp.GetInt64() : 0;
                var charLimit = root.TryGetProperty("character_limit", out var limitProp) ? limitProp.GetInt64() : 0;

                return new ApiValidationResult(
                    true,
                    $"Connected successfully. Tier: {tier} ({charCount:N0}/{charLimit:N0} credits used).");
            }

            // 2. If subscription endpoint returned 401 or 403, key may be scoped (e.g. Voices or TTS-only).
            if (subResponse.StatusCode == HttpStatusCode.Unauthorized || subResponse.StatusCode == HttpStatusCode.Forbidden)
            {
                // 2a. Try Voices endpoint
                using var voiceRequest = new HttpRequestMessage(HttpMethod.Get, VoicesEndpoint);
                voiceRequest.Headers.Add("xi-api-key", cleanKey);

                using var voiceResponse = await _httpClient.SendAsync(voiceRequest, cancellationToken);
                if (voiceResponse.IsSuccessStatusCode)
                {
                    return new ApiValidationResult(true, "Connected successfully. (Restricted key: voices & TTS active).");
                }

                // 2b. If Voices also returned 401/403, test TTS endpoint directly with an empty dry-run payload.
                // We use George (JBFqnCBsd6RMkjVDRZzb), an official premade voice accessible to all accounts including Free tier.
                const string ttsEndpoint = "https://api.elevenlabs.io/v1/text-to-speech/JBFqnCBsd6RMkjVDRZzb";
                using var ttsRequest = new HttpRequestMessage(HttpMethod.Post, ttsEndpoint);
                ttsRequest.Headers.Add("xi-api-key", cleanKey);
                ttsRequest.Content = new StringContent("{\"text\":\"\"}", Encoding.UTF8, "application/json");

                using var ttsResponse = await _httpClient.SendAsync(ttsRequest, cancellationToken);
                if (ttsResponse.IsSuccessStatusCode)
                {
                    return new ApiValidationResult(true, "Connected successfully. (TTS access verified).");
                }

                var errorBody = await ttsResponse.Content.ReadAsStringAsync(cancellationToken);

                // If ElevenLabs returned a "Free users cannot use library voices" notice,
                // authentication succeeded and the account is verified as an active Free account!
                if (errorBody.Contains("Free users cannot use library voices", StringComparison.OrdinalIgnoreCase))
                {
                    return new ApiValidationResult(true, "Connected successfully. (Free tier verified — use premade voices like George).");
                }

                // HTTP 422 (Unprocessable Entity) or 400 validation error confirms authentication & TTS scope succeeded!
                if ((int)ttsResponse.StatusCode == 422 || ttsResponse.StatusCode == HttpStatusCode.BadRequest)
                {
                    if (errorBody.Contains("validation", StringComparison.OrdinalIgnoreCase) ||
                        errorBody.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                        errorBody.Contains("detail", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ApiValidationResult(true, "Connected successfully. (TTS-only key verified, 0 credits used).");
                    }
                }

                // If TTS endpoint also failed with auth error, extract the detailed error from ElevenLabs
                if (string.IsNullOrWhiteSpace(errorBody))
                {
                    errorBody = await voiceResponse.Content.ReadAsStringAsync(cancellationToken);
                }
                if (string.IsNullOrWhiteSpace(errorBody))
                {
                    errorBody = await subResponse.Content.ReadAsStringAsync(cancellationToken);
                }

                var detailedError = ParseElevenLabsError(errorBody);
                if (!string.IsNullOrWhiteSpace(detailedError))
                {
                    return new ApiValidationResult(false, $"ElevenLabs error: {detailedError}");
                }

                return new ApiValidationResult(false, "Invalid API key or missing TTS permission. ElevenLabs rejected authentication.");
            }

            if (subResponse.StatusCode == HttpStatusCode.PaymentRequired)
            {
                return new ApiValidationResult(false, "ElevenLabs payment required or monthly quota reached.");
            }

            if ((int)subResponse.StatusCode == 429)
            {
                return new ApiValidationResult(false, "ElevenLabs rate limit exceeded. Please wait before retrying.");
            }

            var subErrorBody = await subResponse.Content.ReadAsStringAsync(cancellationToken);
            var parsedError = ParseElevenLabsError(subErrorBody);
            if (!string.IsNullOrWhiteSpace(parsedError))
            {
                return new ApiValidationResult(false, $"ElevenLabs error: {parsedError}");
            }

            return new ApiValidationResult(false, $"ElevenLabs returned error (HTTP {(int)subResponse.StatusCode}: {subResponse.ReasonPhrase}).");
        }
        catch (TaskCanceledException)
        {
            return new ApiValidationResult(false, "Connection timed out. ElevenLabs was unreachable.");
        }
        catch (HttpRequestException ex)
        {
            return new ApiValidationResult(false, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected error testing ElevenLabs connection.", ex);
            return new ApiValidationResult(false, "An unexpected error occurred during connection test.");
        }
    }

    private static string? ParseElevenLabsError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("detail", out var detailProp))
            {
                if (detailProp.ValueKind == JsonValueKind.String)
                {
                    return detailProp.GetString();
                }

                if (detailProp.ValueKind == JsonValueKind.Object)
                {
                    if (detailProp.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                    {
                        var msg = msgProp.GetString();
                        if (!string.IsNullOrWhiteSpace(msg)) return msg;
                    }

                    if (detailProp.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.String)
                    {
                        var status = statusProp.GetString();
                        if (!string.IsNullOrWhiteSpace(status)) return status;
                    }
                }
            }

            if (root.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
            {
                return messageProp.GetString();
            }
        }
        catch
        {
            // Ignore JSON parse errors
        }

        return null;
    }
}
