using GamingLiveTranslator.Models;

namespace GamingLiveTranslator.Services.Audio;

/// <summary>
/// Service contract for discovering playback endpoints, detecting virtual audio cables
/// (VB-CABLE, VoiceMeeter, VAC), and validating endpoint availability.
/// </summary>
public interface IVirtualAudioRoutingService
{
    /// <summary>
    /// Enumerates all currently active Windows playback (render) audio endpoints.
    /// </summary>
    Task<IReadOnlyList<AudioDevice>> GetPlaybackDevicesAsync();

    /// <summary>
    /// Detects the presence of a known virtual audio playback device (e.g. "CABLE Input" or "VB-Audio").
    /// Returns the matched AudioDevice, or null if no virtual cable driver is installed.
    /// </summary>
    Task<AudioDevice?> DetectVirtualAudioDeviceAsync();

    /// <summary>
    /// Validates whether a specific playback device ID is currently active and reachable on the system.
    /// </summary>
    bool IsDeviceAvailable(string deviceId);
}
