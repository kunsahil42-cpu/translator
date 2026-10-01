using System.Text.Json.Serialization;

namespace GamingLiveTranslator.Models;

/// <summary>
/// Holds non-sensitive configuration and provider preferences.
/// Sensitive credentials are stored via ISecureCredentialStore and excluded from JSON.
/// </summary>
public class ApiSettings
{
    [JsonIgnore]
    public string DeepgramApiKey { get; set; } = string.Empty;

    [JsonIgnore]
    public string TranslationApiKey { get; set; } = string.Empty;

    public string PreferredSttProvider { get; set; } = "Deepgram";
    public string PreferredTranslationProvider { get; set; } = "GoogleTranslate";
    public List<TranslationProviderEntry> TranslationProviderPool { get; set; } = new();
    public string DefaultSourceLanguage { get; set; } = "hi";
    public string DefaultTargetLanguage { get; set; } = "en";
    public string? SelectedMicrophoneId { get; set; }

    // Phase 6: In-Game HUD Overlay Preferences
    public bool IsOverlayEnabled { get; set; } = false;
    public bool IsOverlayClickThrough { get; set; } = false;
    public double OverlayOpacity { get; set; } = 0.85;
    public double OverlayFontSize { get; set; } = 15.0;
    public int OverlayDurationSeconds { get; set; } = 8;
    public int OverlayMaxMessages { get; set; } = 5;

    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public double? OverlayWidth { get; set; }
    public double? OverlayHeight { get; set; }

    // Phase 7: Voice Output (Text-to-Speech) Preferences - Defaults strictly to OFF per master spec
    public bool IsVoiceOutputEnabled { get; set; } = false;
    public string PreferredTtsProvider { get; set; } = "Deepgram";
    public string SelectedTtsVoice { get; set; } = "aura-2-asteria-en";
    public double TtsVolume { get; set; } = 1.0;
    public double TtsSpeed { get; set; } = 1.0;
    public string SelectedPiperVoice { get; set; } = "tsukuyomi-chan-6lang-fp16";

    // Phase 8: Global Push-to-Talk Hotkey Preferences
    public int HotkeyVirtualKey { get; set; } = 0x78; // VK_F9 default
    public string HotkeyDisplayName { get; set; } = "F9";
    public string HotkeyMode { get; set; } = "PushToTalk"; // "PushToTalk" or "Toggle"

    // Phase 10: Virtual Audio Routing Preferences
    public bool PlayTtsThroughSpeakers { get; set; } = true;
    public bool RouteTtsToVirtualDevice { get; set; } = false;
    public string? VirtualAudioDeviceId { get; set; }
    public string? VirtualAudioDeviceName { get; set; }
}
