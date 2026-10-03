namespace GamingLiveTranslator.Models;

/// <summary>
/// Encapsulates the evaluation result of a GitHub release update check.
/// </summary>
public record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string? ReleaseName = null,
    string? ReleaseNotes = null);
