using GamingLiveTranslator.Models;

namespace GamingLiveTranslator.Services.Audio;

/// <summary>
/// Core audio capture service interface for enumerating hardware microphones and capturing raw PCM streams.
/// </summary>
public interface IMicrophoneService : IDisposable
{
    Task<IReadOnlyList<AudioDevice>> GetAudioDevicesAsync();
    Task<AudioDevice?> GetDefaultDeviceAsync();
    Task StartCaptureAsync(AudioDevice? device = null);
    Task StopCaptureAsync();
    bool IsCapturing { get; }
    AudioDevice? CurrentDevice { get; }
    event EventHandler<AudioCapturedEventArgs>? AudioDataAvailable;
    event EventHandler<CaptureErrorEventArgs>? CaptureError;
}
