using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Updates;

/// <summary>
/// Checks GitHub Releases REST API for available application updates.
/// Runs completely keyless, non-blocking, and fails silently on any error.
/// </summary>
public class UpdateCheckService : IUpdateCheckService, IDisposable
{
    private const string DefaultOwner = "kunsahil42-cpu";
    private const string DefaultRepo = "translator";
    private const string DefaultUserAgent = "GamingLiveTranslator-UpdateChecker";

    private readonly string _owner;
    private readonly string _repo;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Optional tag simulation override for testing without publishing real GitHub releases.
    /// Can also be driven via the GLT_SIMULATE_UPDATE_TAG environment variable.
    /// </summary>
    public string? SimulatedTag { get; set; }

    public string CurrentVersion { get; }

    public UpdateCheckService(
        string owner = DefaultOwner,
        string repo = DefaultRepo,
        HttpClient? httpClient = null)
    {
        _owner = string.IsNullOrWhiteSpace(owner) ? DefaultOwner : owner;
        _repo = string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            _ownsHttpClient = true;
        }

        CurrentVersion = ResolveCurrentVersion();
        SimulatedTag = Environment.GetEnvironmentVariable("GLT_SIMULATE_UPDATE_TAG");
    }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        // 1. Check for local test simulation first
        if (!string.IsNullOrWhiteSpace(SimulatedTag))
        {
            Logger.Info($"[UpdateCheck] Using simulated GitHub release tag: {SimulatedTag}");
            var isSimulatedNewer = CompareVersions(CurrentVersion, SimulatedTag, out var cleanSimLatest);
            return new UpdateCheckResult(
                IsUpdateAvailable: isSimulatedNewer,
                CurrentVersion: CurrentVersion,
                LatestVersion: cleanSimLatest,
                ReleaseUrl: $"https://github.com/{_owner}/{_repo}/releases",
                ReleaseName: $"Release {SimulatedTag} (Simulated)",
                ReleaseNotes: "Test release notes for simulated update notification."
            );
        }

        // 2. Query GitHub Releases REST API
        var requestUrl = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.UserAgent.ParseAdd(DefaultUserAgent);
            request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // If the repository exists but has no published releases yet, GitHub returns 404 Not Found.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Info($"[UpdateCheck] No published releases found on GitHub repo {_owner}/{_repo}.");
                return new UpdateCheckResult(
                    IsUpdateAvailable: false,
                    CurrentVersion: CurrentVersion,
                    LatestVersion: CurrentVersion,
                    ReleaseUrl: $"https://github.com/{_owner}/{_repo}/releases"
                );
            }

            // Rate-limited (HTTP 403 Forbidden) or unexpected error
            if (!response.IsSuccessStatusCode)
            {
                Logger.Info($"[UpdateCheck] GitHub API returned status {(int)response.StatusCode} {response.ReasonPhrase}. Skipping check.");
                return null;
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElement))
            {
                Logger.Info("[UpdateCheck] Response JSON missing tag_name property.");
                return null;
            }

            var rawTag = tagElement.GetString() ?? string.Empty;
            var htmlUrl = root.TryGetProperty("html_url", out var urlElement)
                ? (urlElement.GetString() ?? $"https://github.com/{_owner}/{_repo}/releases")
                : $"https://github.com/{_owner}/{_repo}/releases";

            var releaseName = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var releaseBody = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;

            var isNewer = CompareVersions(CurrentVersion, rawTag, out var cleanLatest);

            Logger.Info($"[UpdateCheck] Checked GitHub releases: latest={rawTag}, current={CurrentVersion}, updateAvailable={isNewer}");

            return new UpdateCheckResult(
                IsUpdateAvailable: isNewer,
                CurrentVersion: CurrentVersion,
                LatestVersion: cleanLatest,
                ReleaseUrl: htmlUrl,
                ReleaseName: releaseName,
                ReleaseNotes: releaseBody
            );
        }
        catch (HttpRequestException ex)
        {
            // Offline, DNS failure, or connection reset
            Logger.Info($"[UpdateCheck] Network unreachable or offline: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            // Request timed out or cancelled on app exit
            Logger.Info("[UpdateCheck] Request timed out or was cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            // Fail silently on any unexpected error
            Logger.Info($"[UpdateCheck] Check failed gracefully: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Compares a local version string against a remote GitHub release tag string using semantic versioning.
    /// Returns true if remote is strictly greater than local.
    /// </summary>
    public static bool CompareVersions(string localVersionString, string remoteTagString, out string cleanRemoteVersion)
    {
        cleanRemoteVersion = remoteTagString;

        if (string.IsNullOrWhiteSpace(remoteTagString))
            return false;

        var cleanRemote = remoteTagString.Trim();
        if (cleanRemote.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            cleanRemote = cleanRemote[1..];

        // Strip prerelease or build metadata for version number comparison
        var dashIndex = cleanRemote.IndexOfAny(new[] { '-', '+' });
        var remoteVersionPart = dashIndex >= 0 ? cleanRemote[..dashIndex] : cleanRemote;

        cleanRemoteVersion = cleanRemote;

        if (!TryParseVersion(localVersionString, out var localVer))
            return false;

        if (!TryParseVersion(remoteVersionPart, out var remoteVer))
            return false;

        return remoteVer > localVer;
    }

    public static bool TryParseVersion(string versionString, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(versionString))
            return false;

        var clean = versionString.Trim();
        if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            clean = clean[1..];

        var dashIndex = clean.IndexOfAny(new[] { '-', '+' });
        if (dashIndex >= 0)
            clean = clean[..dashIndex];

        // System.Version requires at least 2 components (major.minor)
        if (!clean.Contains('.'))
            clean += ".0";

        return Version.TryParse(clean, out version!);
    }

    private static string ResolveCurrentVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateCheckService).Assembly;
            var infoVerAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (infoVerAttr != null && !string.IsNullOrWhiteSpace(infoVerAttr.InformationalVersion))
            {
                var ver = infoVerAttr.InformationalVersion.Split(new[] { '+', '-' })[0];
                if (!string.IsNullOrWhiteSpace(ver))
                    return ver.Trim();
            }

            var asmVersion = assembly.GetName().Version;
            if (asmVersion != null && (asmVersion.Major > 0 || asmVersion.Minor > 0))
            {
                return asmVersion.Build >= 0
                    ? $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}"
                    : $"{asmVersion.Major}.{asmVersion.Minor}.0";
            }

            if (!string.IsNullOrEmpty(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            {
                var fvi = FileVersionInfo.GetVersionInfo(Environment.ProcessPath);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                {
                    var ver = fvi.ProductVersion.Split(new[] { '+', '-' })[0].Trim();
                    if (!string.IsNullOrWhiteSpace(ver))
                        return ver;
                }
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                {
                    return fvi.FileVersion.Trim();
                }
            }
        }
        catch
        {
            // Fallback
        }

        return "2.0.0";
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
