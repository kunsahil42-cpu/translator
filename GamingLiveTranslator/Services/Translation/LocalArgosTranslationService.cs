using System.Net.Http;
using System.Text;
using System.Text.Json;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Translation;

/// <summary>
/// Offline translation service powered by the local Argos Translate subprocess.
/// Connects strictly over loopback HTTP (127.0.0.1).
/// </summary>
public class LocalArgosTranslationService : ITranslationService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HttpClient _packageHttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly ArgosProcessManager _processManager;

    public LocalArgosTranslationService(ArgosProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    }

    public Task<IReadOnlyList<Language>> GetSupportedLanguagesAsync()
    {
        return Task.FromResult(Language.GetInitialLanguages(includeAutoDetect: false));
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TranslationResult.Success(string.Empty);

        // Ensure offline engine is running
        var isStarted = await _processManager.EnsureStartedAsync(cancellationToken);
        if (!isStarted || !_processManager.IsRunning)
        {
            return TranslationResult.Fail(
                $"Offline engine is not running ({_processManager.StatusMessage}).",
                TranslationFailureType.EngineUnavailable);
        }

        try
        {
            // If source is auto, select reasonable default based on target language
            var fromCode = sourceLanguage;
            if (fromCode.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                fromCode = targetLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "hi" : "en";
            }
            var argosFrom = NormalizeCodeForArgos(fromCode);
            var argosTo = NormalizeCodeForArgos(targetLanguage);

            var payload = new
            {
                q = text,
                source = argosFrom,
                target = argosTo
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"http://127.0.0.1:{_processManager.Port}/translate";
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    using var errDoc = JsonDocument.Parse(responseString);
                    if (errDoc.RootElement.TryGetProperty("error", out var errProp))
                    {
                        var errText = errProp.GetString() ?? string.Empty;
                        if (errText.Contains("not installed", StringComparison.OrdinalIgnoreCase) ||
                            errText.Contains("Package not found", StringComparison.OrdinalIgnoreCase) ||
                            errText.Contains("NoneType", StringComparison.OrdinalIgnoreCase))
                        {
                            return TranslationResult.Fail(
                                $"Offline language model for {argosFrom}→{argosTo} is not installed. Open Settings → Offline Language Packages to download it.",
                                TranslationFailureType.EngineUnavailable);
                        }
                        return TranslationResult.Fail(errText, TranslationFailureType.TransientServerError);
                    }
                }
                catch
                {
                    // Fall back to status code
                }

                return TranslationResult.Fail($"Offline translation failed (HTTP {response.StatusCode}).", TranslationFailureType.TransientServerError);
            }

            using var doc = JsonDocument.Parse(responseString);
            if (doc.RootElement.TryGetProperty("translatedText", out var translatedProp))
            {
                var translatedText = translatedProp.GetString() ?? string.Empty;
                return TranslationResult.Success(translatedText, fromCode, text.Length);
            }

            return TranslationResult.Fail("Unexpected response from offline translation engine.", TranslationFailureType.TransientServerError);
        }
        catch (Exception ex)
        {
            Logger.Error("Error during local Argos translation call.", ex);
            return TranslationResult.Fail($"Offline translation error: {ex.Message}", TranslationFailureType.EngineUnavailable);
        }
    }

    /// <summary>
    /// Queries the local engine for currently installed language package pairs.
    /// </summary>
    public async Task<List<string>> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
    {
        if (!_processManager.IsRunning)
            return new List<string>();

        try
        {
            var url = $"http://127.0.0.1:{_processManager.Port}/packages";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("installed", out var installedArr))
                {
                    var result = new List<string>();
                    foreach (var item in installedArr.EnumerateArray())
                    {
                        var code = item.GetString();
                        if (!string.IsNullOrEmpty(code))
                            result.Add(code);
                    }
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to query installed Argos packages.", ex);
        }

        return new List<string>();
    }

    /// <summary>
    /// Tells the local engine to download and install a language package pair.
    /// Uses extended 5-minute timeout because model packages are 45-95MB.
    /// </summary>
    public async Task<bool> InstallPackageAsync(string fromCode, string toCode, CancellationToken cancellationToken = default)
    {
        var isStarted = await _processManager.EnsureStartedAsync(cancellationToken);
        if (!isStarted)
            return false;

        try
        {
            var argosFrom = NormalizeCodeForArgos(fromCode);
            var argosTo = NormalizeCodeForArgos(toCode);

            var payload = new { from_code = argosFrom, to_code = argosTo };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"http://127.0.0.1:{_processManager.Port}/packages/install";
            var response = await _packageHttpClient.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.Error($"Argos package install {argosFrom}→{argosTo} failed (HTTP {response.StatusCode}): {errContent}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to download Argos package {fromCode}→{toCode}.", ex);
            return false;
        }
    }

    private static string NormalizeCodeForArgos(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "en";

        // Argos Translate models use 'zh' for Chinese (Simplified/Traditional)
        if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh";

        // Strip country/region subtag (e.g. en-US -> en, pt-BR -> pt)
        var hyphenIndex = code.IndexOf('-');
        if (hyphenIndex > 0)
            return code.Substring(0, hyphenIndex).ToLowerInvariant();

        return code.ToLowerInvariant();
    }
}
