namespace GamingLiveTranslator.Services.Audio;

/// <summary>
/// Event arguments carrying raw audio bytes and instantaneous volume level from microphone capture.
/// Buffer is guaranteed to be 16kHz, 16-bit, mono raw PCM.
/// </summary>
public class AudioCapturedEventArgs : EventArgs
{
    public byte[] Buffer { get; }
    public int BytesRecorded { get; }
    public float PeakLevel { get; }

    public AudioCapturedEventArgs(byte[] buffer, int bytesRecorded, float peakLevel)
    {
        Buffer = buffer;
        BytesRecorded = bytesRecorded;
        PeakLevel = peakLevel;
    }
}
