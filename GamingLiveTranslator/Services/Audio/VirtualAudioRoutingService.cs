using System.Runtime.InteropServices;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Utilities;
using NAudio.CoreAudioApi;

namespace GamingLiveTranslator.Services.Audio;

/// <summary>
/// Provides Windows WASAPI playback endpoint enumeration and virtual cable auto-detection.
/// </summary>
public class VirtualAudioRoutingService : IVirtualAudioRoutingService
{
    private static readonly string[] VirtualDevicePatterns = new[]
    {
        "cable input",      // VB-Audio CABLE Input (standard)
        "vb-audio",         // VB-Audio products
        "voicemeeter",      // VoiceMeeter virtual inputs
        "virtual cable",    // Generic Virtual Audio Cable
        "vac"               // VAC shorthand
    };

    public Task<IReadOnlyList<AudioDevice>> GetPlaybackDevicesAsync()
    {
        return Task.Run<IReadOnlyList<AudioDevice>>(() =>
        {
            var result = new List<AudioDevice>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var defaultId = string.Empty;

                try
                {
                    using var defaultEndpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                    defaultId = defaultEndpoint?.ID ?? string.Empty;
                }
                catch (COMException)
                {
                    // No default render device exists on system
                }

                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        var isDefault = !string.IsNullOrEmpty(defaultId) &&
                                        string.Equals(endpoint.ID, defaultId, StringComparison.OrdinalIgnoreCase);

                        result.Add(new AudioDevice
                        {
                            Id = endpoint.ID,
                            Name = endpoint.FriendlyName,
                            IsDefault = isDefault,
                            IsInput = false
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to query render endpoint property: {ex.Message}");
                    }
                    finally
                    {
                        endpoint.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to enumerate audio playback devices via WASAPI.", ex);
            }

            return result;
        });
    }

    public async Task<AudioDevice?> DetectVirtualAudioDeviceAsync()
    {
        var devices = await GetPlaybackDevicesAsync();

        // 1. Prioritize official VB-CABLE "CABLE Input"
        var vbCable = devices.FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
        if (vbCable != null)
        {
            Logger.Info($"Detected primary virtual audio cable: {vbCable.Name}");
            return vbCable;
        }

        // 2. Match other common virtual devices (VoiceMeeter, generic virtual cables)
        foreach (var pattern in VirtualDevicePatterns)
        {
            var match = devices.FirstOrDefault(d => d.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                Logger.Info($"Detected virtual audio device ({pattern}): {match.Name}");
                return match;
            }
        }

        return null;
    }

    public bool IsDeviceAvailable(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(deviceId);
            return device != null && device.State == DeviceState.Active;
        }
        catch
        {
            return false;
        }
    }
}
