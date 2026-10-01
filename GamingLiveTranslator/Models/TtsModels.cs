using System.ComponentModel;
using System.Runtime.CompilerServices;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Models;

/// <summary>
/// Synthesis configuration options passed to ITextToSpeechService.
/// </summary>
public record TtsOptions(
    string? Voice = null,
    double Volume = 1.0,
    double Speed = 1.0
);

/// <summary>
/// Describes an available voice option for either Deepgram or Piper.
/// </summary>
public record TtsVoiceOption(
    string Id,
    string DisplayName,
    string LanguageCode,
    string Provider,
    string Gender
);

/// <summary>
/// Event arguments raised when a TTS provider encounters a synthesis or connection failure.
/// </summary>
public class TtsErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }

    public TtsErrorEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }
}

/// <summary>
/// ViewModel representing an offline Piper voice model package (download-on-first-use pattern).
/// </summary>
public class PiperVoicePackage : ObservableObject
{
    private bool _isInstalled;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _statusText = "Not installed";

    public string Id { get; }
    public string DisplayName { get; }
    public string LanguageCode { get; }
    public string ApproxSize { get; }
    public string ModelFileName { get; }
    public string ConfigFileName { get; }
    public string License { get; }
    public string ModelUrl { get; }
    public string ConfigUrl { get; }

    public bool IsInstalled
    {
        get => _isInstalled;
        set => SetProperty(ref _isInstalled, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public System.Windows.Input.ICommand DownloadCommand { get; set; } = null!;

    public PiperVoicePackage(
        string id,
        string displayName,
        string languageCode,
        string approxSize,
        string modelFileName,
        string configFileName,
        string license,
        string modelUrl,
        string configUrl)
    {
        Id = id;
        DisplayName = displayName;
        LanguageCode = languageCode;
        ApproxSize = approxSize;
        ModelFileName = modelFileName;
        ConfigFileName = configFileName;
        License = license;
        ModelUrl = modelUrl;
        ConfigUrl = configUrl;
    }
}
