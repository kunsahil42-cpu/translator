namespace GamingLiveTranslator.Services.Audio;

/// <summary>
/// Event arguments for audio capture errors (e.g. permission denial, device unplugged).
/// </summary>
public class CaptureErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }

    public CaptureErrorEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }
}
