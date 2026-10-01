using System.Windows;
using System.Windows.Input;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Audio;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Services.Speech;
using GamingLiveTranslator.Services.TextToSpeech;
using GamingLiveTranslator.Services.Translation;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.ViewModels;

/// <summary>
/// ViewModel for the main Dashboard view reflecting real audio capture availability,
/// Deepgram streaming connection health, and selected translation direction.
/// </summary>
public class DashboardViewModel : ViewModelBase
{
    private readonly IMicrophoneService _microphoneService;
    private readonly ISpeechToTextService _speechService;
    private readonly SettingsService _settingsService;
    private readonly ITranslationPoolService? _translationPoolService;
    private readonly TtsPlaybackService? _ttsPlaybackService;

    private string _serviceStatus = "Operational";
    private bool _isServiceConnected = true;
    private string _microphoneStatus = "Checking microphone...";
    private string _sttProviderName = "Deepgram Nova-2 (Idle)";
    private string _translationDirection = "Hindi (hi) → English (en)";
    private string _translationProviderName = "Google Cloud Translation";
    private string? _fallbackNotice;
    private bool _isVoiceOutputEnabled;
    private string _voiceRoutingSummary = "Local Speakers Only";
    private string _latency = "~120 ms";
    private string _hotkeySummary = "Push-to-Talk: [F9] (Hold)";

    public string VoiceRoutingSummary
    {
        get => _voiceRoutingSummary;
        set => SetProperty(ref _voiceRoutingSummary, value);
    }

    public string? FallbackNotice
    {
        get => _fallbackNotice;
        set
        {
            if (SetProperty(ref _fallbackNotice, value))
            {
                OnPropertyChanged(nameof(HasFallbackNotice));
            }
        }
    }

    public bool HasFallbackNotice => !string.IsNullOrWhiteSpace(FallbackNotice);

    public string HotkeySummary
    {
        get => _hotkeySummary;
        set => SetProperty(ref _hotkeySummary, value);
    }

    public string ServiceStatus
    {
        get => _serviceStatus;
        set => SetProperty(ref _serviceStatus, value);
    }

    public bool IsServiceConnected
    {
        get => _isServiceConnected;
        set => SetProperty(ref _isServiceConnected, value);
    }

    public string MicrophoneStatus
    {
        get => _microphoneStatus;
        set => SetProperty(ref _microphoneStatus, value);
    }

    public string SttProviderName
    {
        get => _sttProviderName;
        set => SetProperty(ref _sttProviderName, value);
    }

    public string TranslationDirection
    {
        get => _translationDirection;
        set => SetProperty(ref _translationDirection, value);
    }

    public string TranslationProviderName
    {
        get => _translationProviderName;
        set => SetProperty(ref _translationProviderName, value);
    }

    public bool IsVoiceOutputEnabled
    {
        get => _isVoiceOutputEnabled;
        set => SetProperty(ref _isVoiceOutputEnabled, value);
    }

    public string Latency
    {
        get => _latency;
        set => SetProperty(ref _latency, value);
    }

    public ICommand StartTranslatorCommand { get; }

    public DashboardViewModel(
        Action? navigateToTranslator = null,
        IMicrophoneService? microphoneService = null,
        ISpeechToTextService? speechService = null,
        SettingsService? settingsService = null,
        ITranslationPoolService? translationPoolService = null,
        TtsPlaybackService? ttsPlaybackService = null)
    {
        Title = "Dashboard";
        _microphoneService = microphoneService ?? new MicrophoneService();
        _speechService = speechService ?? new DeepgramSpeechService();
        _settingsService = settingsService ?? new SettingsService();
        _translationPoolService = translationPoolService;
        _ttsPlaybackService = ttsPlaybackService;

        StartTranslatorCommand = new RelayCommand(() => navigateToTranslator?.Invoke());

        _speechService.StateChanged += OnSpeechStateChanged;

        if (_translationPoolService != null)
        {
            _translationPoolService.PoolStateChanged += OnPoolStateChanged;
        }

        if (_ttsPlaybackService != null)
        {
            _ttsPlaybackService.RoutingNoticeOccurred += OnRoutingNoticeOccurred;
        }

        _ = InitializeDashboardAsync();
    }

    private void OnRoutingNoticeOccurred(string notice)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            FallbackNotice = notice;
        });
    }

    private void OnPoolStateChanged(string activeProviderLabel, string? fallbackNotice)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            UpdateTranslationStatus(activeProviderLabel, fallbackNotice);
        });
    }

    public void UpdateTranslationStatus(string providerLabel, string? fallbackNotice)
    {
        TranslationProviderName = providerLabel;
        FallbackNotice = fallbackNotice;
    }

    public async Task InitializeDashboardAsync()
    {
        await RefreshMicrophoneStatusAsync();
        await RefreshTranslationDirectionAsync();
        await RefreshHotkeyStatusAsync();
        await RefreshVoiceRoutingStatusAsync();
    }

    public async Task RefreshVoiceRoutingStatusAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            _isVoiceOutputEnabled = settings.IsVoiceOutputEnabled;
            OnPropertyChanged(nameof(IsVoiceOutputEnabled));

            string summary;
            if (settings.RouteTtsToVirtualDevice && settings.PlayTtsThroughSpeakers)
            {
                summary = "Dual Output (Speakers + Virtual Cable)";
            }
            else if (settings.RouteTtsToVirtualDevice && !settings.PlayTtsThroughSpeakers)
            {
                summary = "Virtual Audio Cable (Discord/Voice Chat)";
            }
            else if (!settings.RouteTtsToVirtualDevice && settings.PlayTtsThroughSpeakers)
            {
                summary = "Local Speakers Only";
            }
            else
            {
                summary = "Output Muted";
            }

            VoiceRoutingSummary = summary;
        }
        catch (Exception ex)
        {
            Logger.Error("Error refreshing voice routing status on dashboard.", ex);
        }
    }

    public async Task RefreshHotkeyStatusAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var key = string.IsNullOrEmpty(settings.HotkeyDisplayName) ? "F9" : settings.HotkeyDisplayName;
            var mode = string.IsNullOrEmpty(settings.HotkeyMode) ? "PushToTalk" : settings.HotkeyMode;
            UpdateHotkey(key, mode);
        }
        catch (Exception ex)
        {
            Logger.Error("Error loading hotkey status for dashboard.", ex);
        }
    }

    public void UpdateHotkey(string keyName, string mode)
    {
        var modeDesc = mode.Equals("PushToTalk", StringComparison.OrdinalIgnoreCase) ? "Hold to speak" : "Toggle";
        HotkeySummary = $"Push-to-Talk: [{keyName}] ({modeDesc})";
    }

    public async Task RefreshMicrophoneStatusAsync()
    {
        try
        {
            var defaultDevice = await _microphoneService.GetDefaultDeviceAsync();
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (defaultDevice != null)
                {
                    MicrophoneStatus = $"{defaultDevice.Name} (Ready)";
                }
                else
                {
                    MicrophoneStatus = "No Microphone Detected (Not Available)";
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Error checking microphone status on dashboard.", ex);
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                MicrophoneStatus = "Microphone Error (Not Available)";
            });
        }
    }

    public async Task RefreshTranslationDirectionAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var source = settings.DefaultSourceLanguage ?? "hi";
            var target = settings.DefaultTargetLanguage ?? "en";

            var allLangs = Language.GetInitialLanguages(includeAutoDetect: true);
            var srcObj = allLangs.FirstOrDefault(l => l.Code.Equals(source, StringComparison.OrdinalIgnoreCase));
            var tgtObj = allLangs.FirstOrDefault(l => l.Code.Equals(target, StringComparison.OrdinalIgnoreCase));

            var srcName = srcObj?.DisplayName ?? source;
            var tgtName = tgtObj?.DisplayName ?? target;

            string providerTitle = "Translation Pool";
            string? notice = _translationPoolService?.CurrentFallbackNotice;

            if (_translationPoolService != null)
            {
                if (!string.IsNullOrWhiteSpace(_translationPoolService.CurrentActiveProviderLabel))
                {
                    providerTitle = _translationPoolService.CurrentActiveProviderLabel;
                }
                else
                {
                    var pool = await _translationPoolService.GetPoolEntriesAsync();
                    var top = pool.FirstOrDefault(e => e.IsEnabled);
                    if (top != null)
                    {
                        providerTitle = top.Label;
                    }
                }
            }
            else
            {
                var isArgos = (settings.PreferredTranslationProvider ?? "").Equals("ArgosTranslate", StringComparison.OrdinalIgnoreCase);
                providerTitle = isArgos ? "Argos Translate (100% Offline)" : "Google Cloud Translation";
            }

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                TranslationDirection = $"{srcName} → {tgtName}";
                TranslationProviderName = providerTitle;
                FallbackNotice = notice;
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Error loading translation direction for dashboard.", ex);
        }
    }

    public void UpdateProvider(string providerId)
    {
        var isArgos = providerId.Equals("ArgosTranslate", StringComparison.OrdinalIgnoreCase);
        TranslationProviderName = isArgos ? "Argos Translate (100% Offline)" : "Google Cloud Translation";
        FallbackNotice = null;
    }

    private void OnSpeechStateChanged(object? sender, ConnectionState state)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            SttProviderName = state switch
            {
                ConnectionState.Connected => "Deepgram Nova-2 (Connected)",
                ConnectionState.Connecting => "Deepgram Nova-2 (Connecting...)",
                ConnectionState.Reconnecting => "Deepgram Nova-2 (Reconnecting...)",
                ConnectionState.Failed => "Deepgram Nova-2 (Connection Failed)",
                _ => "Deepgram Nova-2 (Idle)"
            };

            IsServiceConnected = state == ConnectionState.Connected || state == ConnectionState.Disconnected;
            ServiceStatus = state == ConnectionState.Connected ? "Active Stream" : "Operational";
        });
    }
}
