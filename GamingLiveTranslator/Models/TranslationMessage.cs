using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Models;

/// <summary>
/// Represents a single live translation entry. Inherits from ObservableObject so
/// in-flight asynchronous translation updates refresh the WPF UI in real time.
/// </summary>
public class TranslationMessage : ObservableObject
{
    private string _originalText = string.Empty;
    private string _translatedText = string.Empty;
    private bool _isTranslating;
    private bool _isTranslationFailed;

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Speaker { get; set; } = "You";

    public string OriginalText
    {
        get => _originalText;
        set => SetProperty(ref _originalText, value);
    }

    public string TranslatedText
    {
        get => _translatedText;
        set => SetProperty(ref _translatedText, value);
    }

    public bool IsTranslating
    {
        get => _isTranslating;
        set => SetProperty(ref _isTranslating, value);
    }

    public bool IsTranslationFailed
    {
        get => _isTranslationFailed;
        set => SetProperty(ref _isTranslationFailed, value);
    }

    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public bool IsFinal { get; set; } = true;
    public double Confidence { get; set; } = 1.0;
}
