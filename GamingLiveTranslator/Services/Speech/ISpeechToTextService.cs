namespace GamingLiveTranslator.Services.Speech;

/// <summary>
/// Represents the connection lifecycle of the speech-to-text service.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>
/// Configuration options for establishing a speech-to-text streaming session.
/// </summary>
public class SpeechToTextOptions
{
    public string SourceLanguage { get; set; } = "hi";
    public string Model { get; set; } = "nova-2";
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
    public bool InterimResults { get; set; } = true;
    public bool SmartFormat { get; set; } = true;
    public int EndpointingMs { get; set; } = 300;
}

/// <summary>
/// Event arguments containing transcribed text, confidence, and finality flags.
/// </summary>
public class TranscriptReceivedEventArgs : EventArgs
{
    public string Transcript { get; }
    public bool IsFinal { get; }
    public bool IsSpeechFinal { get; }
    public double Confidence { get; }

    public TranscriptReceivedEventArgs(string transcript, bool isFinal, bool isSpeechFinal, double confidence = 1.0)
    {
        Transcript = transcript;
        IsFinal = isFinal;
        IsSpeechFinal = isSpeechFinal;
        Confidence = confidence;
    }
}

/// <summary>
/// Event arguments for speech service error notifications.
/// </summary>
public class SpeechServiceErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }

    public SpeechServiceErrorEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }
}

/// <summary>
/// Core speech-to-text service interface for real-time streaming transcription.
/// </summary>
public interface ISpeechToTextService : IDisposable
{
    ConnectionState State { get; }
    Task ConnectAsync(SpeechToTextOptions options, string apiKey, CancellationToken cancellationToken = default);
    Task SendAudioAsync(byte[] audioBuffer, CancellationToken cancellationToken = default);
    Task FinalizeAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    event EventHandler<TranscriptReceivedEventArgs>? InterimTranscriptReceived;
    event EventHandler<TranscriptReceivedEventArgs>? FinalTranscriptReceived;
    event EventHandler<SpeechServiceErrorEventArgs>? ErrorOccurred;
    event EventHandler<ConnectionState>? StateChanged;
}
