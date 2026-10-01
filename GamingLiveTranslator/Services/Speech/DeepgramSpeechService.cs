using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Web;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Speech;

/// <summary>
/// Deepgram real-time streaming speech-to-text service via WebSocket (wss://api.deepgram.com/v1/listen).
/// Serializes binary audio sends, runs background KeepAlive pings every 4 seconds, and parses interim vs final transcripts.
/// </summary>
public class DeepgramSpeechService : ISpeechToTextService
{
    private const string BaseWebSocketUrl = "wss://api.deepgram.com/v1/listen";

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _sessionCts;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Task? _receiveTask;
    private Task? _keepAliveTask;

    private ConnectionState _state = ConnectionState.Disconnected;
    private SpeechToTextOptions? _currentOptions;
    private string? _currentApiKey;
    private int _reconnectAttempts;
    private const int MaxReconnectAttempts = 3;
    private bool _isDisposed;

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<TranscriptReceivedEventArgs>? InterimTranscriptReceived;
    public event EventHandler<TranscriptReceivedEventArgs>? FinalTranscriptReceived;
    public event EventHandler<SpeechServiceErrorEventArgs>? ErrorOccurred;
    public event EventHandler<ConnectionState>? StateChanged;

    public async Task ConnectAsync(SpeechToTextOptions options, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Deepgram API key cannot be empty.", nameof(apiKey));

        _currentOptions = options ?? throw new ArgumentNullException(nameof(options));
        _currentApiKey = apiKey;
        _reconnectAttempts = 0;

        await InternalConnectAsync(cancellationToken);
    }

    private async Task InternalConnectAsync(CancellationToken cancellationToken)
    {
        State = _reconnectAttempts > 0 ? ConnectionState.Reconnecting : ConnectionState.Connecting;

        try
        {
            CleanupWebSocket();

            _sessionCts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();

            // Set authentication header for Deepgram WebSocket handshake
            _webSocket.Options.SetRequestHeader("Authorization", $"Token {_currentApiKey}");

            var uri = BuildWebSocketUri(_currentOptions!);
            Logger.Info($"Connecting to Deepgram streaming WebSocket endpoint: {uri.GetLeftPart(UriPartial.Path)}");

            using var linkCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCts.Token);
            await _webSocket.ConnectAsync(uri, linkCts.Token);

            State = ConnectionState.Connected;
            _reconnectAttempts = 0;
            Logger.Info("Successfully established Deepgram real-time streaming connection.");

            _receiveTask = Task.Run(() => ReceiveLoopAsync(_sessionCts.Token));
            _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(_sessionCts.Token));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to connect to Deepgram streaming WebSocket.", ex);
            State = ConnectionState.Failed;
            ErrorOccurred?.Invoke(this, new SpeechServiceErrorEventArgs("Failed to connect to Deepgram. Please check your internet connection and API key.", ex));
            throw;
        }
    }

    private static Uri BuildWebSocketUri(SpeechToTextOptions options)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["encoding"] = "linear16";
        query["sample_rate"] = options.SampleRate.ToString();
        query["channels"] = options.Channels.ToString();
        query["model"] = options.Model;

        if (!string.IsNullOrWhiteSpace(options.SourceLanguage))
        {
            query["language"] = options.SourceLanguage;
        }

        query["interim_results"] = options.InterimResults.ToString().ToLowerInvariant();
        query["smart_format"] = options.SmartFormat.ToString().ToLowerInvariant();
        query["endpointing"] = options.EndpointingMs.ToString();

        return new Uri($"{BaseWebSocketUrl}?{query}");
    }

    public async Task SendAudioAsync(byte[] audioBuffer, CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected || _webSocket == null || _webSocket.State != WebSocketState.Open)
            return;

        if (audioBuffer == null || audioBuffer.Length == 0)
            return;

        try
        {
            // Drop buffer if previous buffer is still transmitting to prevent socket queue backlog
            if (!await _sendLock.WaitAsync(50, cancellationToken))
                return;

            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(audioBuffer),
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        cancellationToken);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to send audio buffer to WebSocket: {ex.Message}");
        }
    }

    public async Task FinalizeAsync(CancellationToken cancellationToken = default)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            return;

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    var finalizeMsg = Encoding.UTF8.GetBytes("{\"type\":\"Finalize\"}");
                    await _webSocket.SendAsync(new ArraySegment<byte>(finalizeMsg), WebSocketMessageType.Text, true, cancellationToken);
                    Logger.Info("Sent Deepgram Finalize frame to flush pending audio buffers.");
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warn($"Notice while sending Deepgram Finalize frame: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket == null || State == ConnectionState.Disconnected)
            return;

        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                // Send Deepgram graceful CloseStream JSON frame
                // Note for Phase 8 / Push-to-Talk: Deepgram also supports {"type":"Finalize"} to flush
                // buffered audio without terminating the connection. CloseStream terminates the stream completely.
                await _sendLock.WaitAsync(TimeSpan.FromSeconds(2));
                try
                {
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        var closeMsg = Encoding.UTF8.GetBytes("{\"type\":\"CloseStream\"}");
                        await _webSocket.SendAsync(new ArraySegment<byte>(closeMsg), WebSocketMessageType.Text, true, CancellationToken.None);
                        await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
                    }
                }
                finally
                {
                    _sendLock.Release();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Notice during graceful WebSocket closure: {ex.Message}");
        }
        finally
        {
            CleanupWebSocket();
            State = ConnectionState.Disconnected;
            Logger.Info("Disconnected from Deepgram streaming service.");
        }
    }

    /// <summary>
    /// Deepgram documentation specifies sending a KeepAlive message every 3-5 seconds to prevent
    /// the 10-second idle timeout that terminates the connection during periods of speaker silence.
    /// We send {"type": "KeepAlive"} as a text frame every 4 seconds.
    /// </summary>
    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
        var keepAliveBytes = Encoding.UTF8.GetBytes("{\"type\":\"KeepAlive\"}");

        try
        {
            while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    await _sendLock.WaitAsync(cancellationToken);
                    try
                    {
                        if (_webSocket.State == WebSocketState.Open)
                        {
                            await _webSocket.SendAsync(
                                new ArraySegment<byte>(keepAliveBytes),
                                WebSocketMessageType.Text,
                                endOfMessage: true,
                                cancellationToken);
                        }
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warn($"KeepAlive loop exception: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var memoryStream = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    var status = _webSocket?.CloseStatus ?? result.CloseStatus;
                    var desc = _webSocket?.CloseStatusDescription ?? result.CloseStatusDescription ?? "Remote server closed connection";
                    Logger.Warn($"Deepgram WebSocket closed by remote server: {status} ({desc})");
                    HandleUnexpectedDisconnection($"{status}: {desc}");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    memoryStream.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        var json = Encoding.UTF8.GetString(memoryStream.ToArray());
                        memoryStream.SetLength(0);
                        ProcessIncomingMessage(json);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("Exception in WebSocket receive loop.", ex);
            HandleUnexpectedDisconnection(ex.Message);
            return;
        }

        if (State == ConnectionState.Connected)
        {
            HandleUnexpectedDisconnection("Connection terminated unexpectedly.");
        }
    }

    private void ProcessIncomingMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "Error")
            {
                var errMsg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "Unknown error" : "Deepgram returned an error.";
                Logger.Error($"Deepgram server error received: {errMsg}");
                ErrorOccurred?.Invoke(this, new SpeechServiceErrorEventArgs($"Deepgram error: {errMsg}"));
                return;
            }

            if (!root.TryGetProperty("channel", out var channel))
                return;

            if (!channel.TryGetProperty("alternatives", out var alternatives) || alternatives.GetArrayLength() == 0)
                return;

            var primaryAlternative = alternatives[0];
            var transcript = primaryAlternative.TryGetProperty("transcript", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(transcript))
                return;

            double confidence = primaryAlternative.TryGetProperty("confidence", out var c) ? c.GetDouble() : 1.0;

            bool isFinal = root.TryGetProperty("is_final", out var f) && f.GetBoolean();
            bool isSpeechFinal = root.TryGetProperty("speech_final", out var sf) && sf.GetBoolean();

            if (!isFinal)
            {
                InterimTranscriptReceived?.Invoke(this, new TranscriptReceivedEventArgs(transcript, isFinal: false, isSpeechFinal: false, confidence));
            }
            else
            {
                FinalTranscriptReceived?.Invoke(this, new TranscriptReceivedEventArgs(transcript, isFinal: true, isSpeechFinal, confidence));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to parse Deepgram message: {ex.Message}");
        }
    }

    private void HandleUnexpectedDisconnection(string? reason = null)
    {
        if (_sessionCts?.IsCancellationRequested == true)
            return;

        if (_reconnectAttempts < MaxReconnectAttempts)
        {
            _reconnectAttempts++;
            var delayMs = (int)Math.Pow(2, _reconnectAttempts) * 1000;
            Logger.Warn($"Unexpected disconnect ({reason}). Attempting reconnect {_reconnectAttempts}/{MaxReconnectAttempts} in {delayMs}ms...");

            Task.Run(async () =>
            {
                while (_reconnectAttempts <= MaxReconnectAttempts && _sessionCts?.IsCancellationRequested != true)
                {
                    await Task.Delay(delayMs);
                    try
                    {
                        await InternalConnectAsync(CancellationToken.None);
                        return; // Successfully reconnected
                    }
                    catch (Exception rex)
                    {
                        Logger.Warn($"Reconnect attempt {_reconnectAttempts} failed: {rex.Message}");
                        _reconnectAttempts++;
                        delayMs = (int)Math.Pow(2, _reconnectAttempts) * 1000;
                    }
                }

                State = ConnectionState.Failed;
                var msg = !string.IsNullOrWhiteSpace(reason)
                    ? $"Deepgram disconnected: {reason}. Click 'Start Session' to reconnect."
                    : "Connection to Deepgram timed out. Click 'Start Session' to reconnect.";
                ErrorOccurred?.Invoke(this, new SpeechServiceErrorEventArgs(msg));
            });
        }
        else
        {
            State = ConnectionState.Failed;
            var msg = !string.IsNullOrWhiteSpace(reason)
                ? $"Deepgram disconnected: {reason}. Click 'Start Session' to reconnect."
                : "Connection to Deepgram timed out or was disconnected. Please click Start to reconnect.";
            ErrorOccurred?.Invoke(this, new SpeechServiceErrorEventArgs(msg));
        }
    }

    private void CleanupWebSocket()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;

        if (_webSocket != null)
        {
            try { _webSocket.Dispose(); } catch { }
            _webSocket = null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        _sendLock.Dispose();
    }
}
