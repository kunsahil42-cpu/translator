using GamingLiveTranslator.Models;

namespace GamingLiveTranslator.Services.Updates;

/// <summary>
/// Service contract for checking GitHub Releases for newer application versions.
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>
    /// Gets the running application version (e.g., "1.0.0").
    /// </summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Checks GitHub Releases asynchronously for a newer release than the current version.
    /// Fails silently and returns null on network, rate-limiting, or parsing errors.
    /// </summary>
    Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken cancellationToken = default);
}
