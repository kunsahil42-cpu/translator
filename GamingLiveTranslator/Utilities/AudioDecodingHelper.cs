using System.IO;
using NAudio.Wave;

namespace GamingLiveTranslator.Utilities;

/// <summary>
/// Shared audio decoding utilities for converting compressed streams (e.g. MP3)
/// to standard 16-bit linear PCM WAV audio bytes.
/// Ensures standard formatting for downstream peak normalization, presence EQ,
/// and dual-device WASAPI playback pipelines.
/// </summary>
public static class AudioDecodingHelper
{
    /// <summary>
    /// Decodes raw MP3 stream bytes into standard 16-bit linear PCM WAV bytes.
    /// Uses Windows Media Foundation via NAudio, guaranteeing full compatibility
    /// with the downstream peak normalization, presence EQ, and WASAPI dual-output pipeline.
    /// </summary>
    /// <param name="mp3Bytes">The raw MP3 audio payload.</param>
    /// <returns>16-bit linear PCM WAV audio bytes.</returns>
    public static byte[] DecodeMp3ToWavPcm(byte[] mp3Bytes)
    {
        if (mp3Bytes == null || mp3Bytes.Length == 0)
            return Array.Empty<byte>();

        using var mp3Stream = new MemoryStream(mp3Bytes);
        using var reader = new StreamMediaFoundationReader(mp3Stream);
        using var wavStream = new MemoryStream();
        using (var writer = new WaveFileWriter(wavStream, reader.WaveFormat))
        {
            reader.CopyTo(writer);
        }
        return wavStream.ToArray();
    }
}
