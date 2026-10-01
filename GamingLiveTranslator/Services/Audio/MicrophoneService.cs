using System.IO;
using System.Runtime.InteropServices;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Utilities;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GamingLiveTranslator.Services.Audio;

/// <summary>
/// WASAPI microphone capture service using NAudio MMDeviceEnumerator and WasapiCapture.
/// Resamples hardware audio stream to 16kHz, 16-bit, mono PCM for Phase 4 streaming.
/// </summary>
public class MicrophoneService : IMicrophoneService
{
    private WasapiCapture? _capture;
    private MMDevice? _currentMmDevice;
    private AudioDevice? _currentDevice;
    private readonly object _syncLock = new();
    private bool _isDisposed;

    public bool IsCapturing { get; private set; }
    public AudioDevice? CurrentDevice => _currentDevice;

    public event EventHandler<AudioCapturedEventArgs>? AudioDataAvailable;
    public event EventHandler<CaptureErrorEventArgs>? CaptureError;

    public Task<IReadOnlyList<AudioDevice>> GetAudioDevicesAsync()
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
                    using var defaultEndpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                    defaultId = defaultEndpoint?.ID ?? string.Empty;
                }
                catch (COMException)
                {
                    // No default capture device exists on system
                }

                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
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
                            IsInput = true
                        });
                    }
                    finally
                    {
                        endpoint.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to enumerate audio capture devices.", ex);
            }

            return result;
        });
    }

    public async Task<AudioDevice?> GetDefaultDeviceAsync()
    {
        var devices = await GetAudioDevicesAsync();
        return devices.FirstOrDefault(d => d.IsDefault) ?? devices.FirstOrDefault();
    }

    public Task StartCaptureAsync(AudioDevice? device = null)
    {
        lock (_syncLock)
        {
            if (IsCapturing)
                return Task.CompletedTask;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                if (device != null && !string.IsNullOrEmpty(device.Id))
                {
                    try
                    {
                        _currentMmDevice = enumerator.GetDevice(device.Id);
                        _currentDevice = device;
                    }
                    catch (COMException)
                    {
                        throw new InvalidOperationException("The selected microphone was not found or is no longer connected.");
                    }
                }
                else
                {
                    try
                    {
                        _currentMmDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                        _currentDevice = new AudioDevice
                        {
                            Id = _currentMmDevice.ID,
                            Name = _currentMmDevice.FriendlyName,
                            IsDefault = true,
                            IsInput = true
                        };
                    }
                    catch (COMException)
                    {
                        throw new InvalidOperationException("No microphone devices found on this system.");
                    }
                }

                if (_currentMmDevice == null)
                    throw new InvalidOperationException("Could not initialize microphone device.");

                // Initialize fresh WasapiCapture instance (WasapiCapture cannot be restarted once stopped)
                _capture = new WasapiCapture(_currentMmDevice);
                _capture.DataAvailable += OnWasapiDataAvailable;
                _capture.RecordingStopped += OnWasapiRecordingStopped;

                _capture.StartRecording();
                IsCapturing = true;
                Logger.Info($"Started audio capture on device: '{_currentDevice.Name}'");
            }
            catch (COMException comEx)
            {
                IsCapturing = false;
                CleanupCapture();

                // Check for Windows privacy permission denial (E_ACCESSDENIED = 0x80070005)
                if ((uint)comEx.ErrorCode == 0x80070005)
                {
                    var msg = "Microphone access is blocked in Windows privacy settings. Allow access under Settings > Privacy & security > Microphone.";
                    Logger.Warn(msg);
                    CaptureError?.Invoke(this, new CaptureErrorEventArgs(msg, comEx));
                    throw new UnauthorizedAccessException(msg, comEx);
                }

                var generalMsg = $"Microphone initialization failed (HRESULT: 0x{comEx.ErrorCode:X8}).";
                Logger.Error(generalMsg, comEx);
                CaptureError?.Invoke(this, new CaptureErrorEventArgs(generalMsg, comEx));
                throw;
            }
            catch (Exception ex)
            {
                IsCapturing = false;
                CleanupCapture();
                Logger.Error("Error starting microphone capture.", ex);
                CaptureError?.Invoke(this, new CaptureErrorEventArgs(ex.Message, ex));
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        lock (_syncLock)
        {
            if (!IsCapturing || _capture == null)
                return Task.CompletedTask;

            try
            {
                _capture.StopRecording();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception while stopping capture: {ex.Message}");
            }
            finally
            {
                IsCapturing = false;
                CleanupCapture();
                Logger.Info("Stopped microphone capture.");
            }
        }

        return Task.CompletedTask;
    }

    private void OnWasapiDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture == null)
            return;

        try
        {
            var nativeFormat = _capture.WaveFormat;
            var (pcm16Mono, peakLevel) = ConvertTo16kHz16BitMono(e.Buffer, e.BytesRecorded, nativeFormat);

            AudioDataAvailable?.Invoke(this, new AudioCapturedEventArgs(pcm16Mono, pcm16Mono.Length, peakLevel));
        }
        catch (Exception ex)
        {
            Logger.Error("Error processing audio capture buffer.", ex);
        }
    }

    private void OnWasapiRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_syncLock)
        {
            IsCapturing = false;
            CleanupCapture();
        }

        if (e.Exception != null)
        {
            var msg = "Microphone was disconnected or invalidated mid-capture.";
            Logger.Warn($"{msg} Exception: {e.Exception.Message}");
            CaptureError?.Invoke(this, new CaptureErrorEventArgs(msg, e.Exception));
        }
    }

    /// <summary>
    /// Converts WASAPI input buffer (typically IEEE float 32-bit or 16-bit PCM at 44.1k/48k stereo/mono)
    /// to standard 16kHz, 16-bit, mono raw PCM byte buffer and computes peak level (0.0 to 1.0).
    /// </summary>
    private static (byte[] Pcm16Mono, float PeakLevel) ConvertTo16kHz16BitMono(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        int channels = format.Channels;
        int sampleRate = format.SampleRate;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || format.BitsPerSample == 32;

        int bytesPerSample = format.BitsPerSample / 8;
        int totalSamplesInInput = bytesRecorded / bytesPerSample;
        int frames = totalSamplesInInput / channels;

        var monoSamples = new float[frames];
        float peak = 0.0f;

        if (isFloat)
        {
            for (int i = 0; i < frames; i++)
            {
                float sum = 0.0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int offset = (i * channels + ch) * 4;
                    if (offset + 4 <= bytesRecorded)
                    {
                        float sample = BitConverter.ToSingle(buffer, offset);
                        sum += sample;
                    }
                }
                float mono = sum / channels;
                monoSamples[i] = mono;
                float abs = Math.Abs(mono);
                if (abs > peak) peak = abs;
            }
        }
        else
        {
            for (int i = 0; i < frames; i++)
            {
                float sum = 0.0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int offset = (i * channels + ch) * 2;
                    if (offset + 2 <= bytesRecorded)
                    {
                        short sample = BitConverter.ToInt16(buffer, offset);
                        sum += sample / 32768.0f;
                    }
                }
                float mono = sum / channels;
                monoSamples[i] = mono;
                float abs = Math.Abs(mono);
                if (abs > peak) peak = abs;
            }
        }

        peak = Math.Clamp(peak, 0.0f, 1.0f);

        int targetSampleRate = 16000;
        int targetFrames = (int)((long)frames * targetSampleRate / sampleRate);
        if (targetFrames <= 0)
            return (Array.Empty<byte>(), peak);

        var outputBytes = new byte[targetFrames * 2];
        double ratio = (double)sampleRate / targetSampleRate;

        for (int i = 0; i < targetFrames; i++)
        {
            double sourceIndex = i * ratio;
            int indexFloor = (int)sourceIndex;
            int indexCeil = Math.Min(indexFloor + 1, frames - 1);
            double fraction = sourceIndex - indexFloor;

            float interpolated = (float)((1.0 - fraction) * monoSamples[indexFloor] + fraction * monoSamples[indexCeil]);
            short pcmSample = (short)Math.Clamp(interpolated * 32767.0f, -32768.0f, 32767.0f);

            outputBytes[i * 2] = (byte)(pcmSample & 0xFF);
            outputBytes[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
        }

        return (outputBytes, peak);
    }

    private void CleanupCapture()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= OnWasapiDataAvailable;
            _capture.RecordingStopped -= OnWasapiRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        if (_currentMmDevice != null)
        {
            _currentMmDevice.Dispose();
            _currentMmDevice = null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopCaptureAsync().GetAwaiter().GetResult();
        CleanupCapture();
    }
}
