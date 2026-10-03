using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Audio;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Services.Hotkeys;
using GamingLiveTranslator.Services.Speech;
using GamingLiveTranslator.Services.TextToSpeech;
using GamingLiveTranslator.Services.Translation;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.ViewModels;

public class TranslatorViewModel : ViewModelBase
{
    private readonly IMicrophoneService _microphoneService;
    private readonly ISpeechToTextService _speechService;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly ITranslationService _googleTranslationService;
    private readonly LocalArgosTranslationService? _argosTranslationService;
    private readonly ITranslationPoolService _translationPoolService;
    private readonly SettingsService _settingsService;
    private readonly ITextToSpeechService? _ttsService;
    private readonly TtsPlaybackService? _ttsPlaybackService;
    private readonly GlobalHotkeyService? _hotkeyService;

    private AudioDevice? _selectedDevice;
    private bool _isListening;
    private float _currentLevel;
    private int _levelSegments;

    private string _interimText = string.Empty;
    private string _accumulatedFinalText = string.Empty;

    private Language? _selectedSourceLanguage;
    private Language? _selectedTargetLanguage;

    private ConnectionState _deepgramState = ConnectionState.Disconnected;
    private string _deepgramStatusText = "Disconnected";
    private string _statusMessage = "Ready to start live translation session.";

    public ObservableCollection<AudioDevice> AvailableDevices { get; } = new();
    public ObservableCollection<TranslationMessage> TranscriptHistory { get; } = new();
    public ObservableCollection<Language> AvailableSourceLanguages { get; } = new();
    public ObservableCollection<Language> AvailableTargetLanguages { get; } = new();

    public AudioDevice? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    public bool IsListening
    {
        get => _isListening;
        set => SetProperty(ref _isListening, value);
    }

    public float CurrentLevel
    {
        get => _currentLevel;
        set => SetProperty(ref _currentLevel, value);
    }

    public int LevelSegments
    {
        get => _levelSegments;
        set => SetProperty(ref _levelSegments, value);
    }

    public string InterimText
    {
        get => _interimText;
        set => SetProperty(ref _interimText, value);
    }

    public Language? SelectedSourceLanguage
    {
        get => _selectedSourceLanguage;
        set
        {
            if (SetProperty(ref _selectedSourceLanguage, value) && value != null)
            {
                _ = SaveLanguagePreferencesAsync();
                if (IsListening)
                {
                    _ = RestartSessionForLanguageChangeAsync();
                }
            }
        }
    }

    public Language? SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set
        {
            if (SetProperty(ref _selectedTargetLanguage, value) && value != null)
            {
                _ = SaveLanguagePreferencesAsync();
            }
        }
    }

    public ConnectionState DeepgramState
    {
        get => _deepgramState;
        set => SetProperty(ref _deepgramState, value);
    }

    public string DeepgramStatusText
    {
        get => _deepgramStatusText;
        set => SetProperty(ref _deepgramStatusText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand StartCaptureCommand { get; }
    public ICommand StopCaptureCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand SwapLanguagesCommand { get; }
    public ICommand ClearHistoryCommand { get; }

    public TranslatorViewModel(
        IMicrophoneService? microphoneService = null,
        ISpeechToTextService? speechService = null,
        ITranslationService? googleTranslationService = null,
        LocalArgosTranslationService? argosTranslationService = null,
        ISecureCredentialStore? credentialStore = null,
        SettingsService? settingsService = null,
        ITextToSpeechService? ttsService = null,
        TtsPlaybackService? ttsPlaybackService = null,
        GlobalHotkeyService? hotkeyService = null,
        ITranslationPoolService? translationPoolService = null)
    {
        Title = "Live Translator";
        _microphoneService = microphoneService ?? new MicrophoneService();
        _speechService = speechService ?? new DeepgramSpeechService();
        _credentialStore = credentialStore ?? new SecureCredentialStore();
        _googleTranslationService = googleTranslationService ?? new TranslationService(_credentialStore);
        _argosTranslationService = argosTranslationService;
        _settingsService = settingsService ?? new SettingsService();
        _translationPoolService = translationPoolService ?? new TranslationPoolService(
            _settingsService,
            _credentialStore,
            (_googleTranslationService as TranslationService) ?? new TranslationService(_credentialStore),
            new LectoTranslationService(_credentialStore),
            _argosTranslationService);
        _ttsService = ttsService;
        _ttsPlaybackService = ttsPlaybackService;
        _hotkeyService = hotkeyService;

        if (_hotkeyService != null)
        {
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _hotkeyService.HotkeyReleased += OnHotkeyReleased;
        }

        // Audio capture callbacks
        _microphoneService.AudioDataAvailable += OnAudioDataAvailable;
        _microphoneService.CaptureError += OnCaptureError;

        // Speech recognition callbacks
        _speechService.InterimTranscriptReceived += OnInterimTranscriptReceived;
        _speechService.FinalTranscriptReceived += OnFinalTranscriptReceived;
        _speechService.StateChanged += OnSpeechStateChanged;
        _speechService.ErrorOccurred += OnSpeechErrorOccurred;

        StartCaptureCommand = new RelayCommand(async () => await StartSessionAsync(), () => !IsListening && SelectedDevice != null);
        StopCaptureCommand = new RelayCommand(async () => await StopSessionAsync(), () => IsListening);
        RefreshDevicesCommand = new RelayCommand(async () => await LoadDevicesAsync(), () => !IsListening);
        SwapLanguagesCommand = new RelayCommand(SwapLanguages, () => !IsListening && SelectedSourceLanguage?.Code != "auto");
        ClearHistoryCommand = new RelayCommand(() => TranscriptHistory.Clear(), () => TranscriptHistory.Count > 0);

        InitializeLanguages();
        _ = LoadDevicesAsync();
    }

    private void InitializeLanguages()
    {
        AvailableSourceLanguages.Clear();
        foreach (var lang in Language.GetInitialLanguages(includeAutoDetect: true))
        {
            AvailableSourceLanguages.Add(lang);
        }

        AvailableTargetLanguages.Clear();
        foreach (var lang in Language.GetInitialLanguages(includeAutoDetect: false))
        {
            AvailableTargetLanguages.Add(lang);
        }

        _ = LoadLanguagePreferencesAsync();
    }

    private async Task LoadLanguagePreferencesAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var sourceCode = string.IsNullOrWhiteSpace(settings.DefaultSourceLanguage) ? "hi" : settings.DefaultSourceLanguage;
            var targetCode = string.IsNullOrWhiteSpace(settings.DefaultTargetLanguage) ? "en" : settings.DefaultTargetLanguage;

            SelectedSourceLanguage = AvailableSourceLanguages.FirstOrDefault(l => l.Code.Equals(sourceCode, StringComparison.OrdinalIgnoreCase))
                                     ?? AvailableSourceLanguages.FirstOrDefault(l => l.Code == "hi")
                                     ?? AvailableSourceLanguages.FirstOrDefault();

            SelectedTargetLanguage = AvailableTargetLanguages.FirstOrDefault(l => l.Code.Equals(targetCode, StringComparison.OrdinalIgnoreCase))
                                     ?? AvailableTargetLanguages.FirstOrDefault(l => l.Code == "en")
                                     ?? AvailableTargetLanguages.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load language preferences in TranslatorViewModel.", ex);
        }
    }

    private async Task SaveLanguagePreferencesAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            if (SelectedSourceLanguage != null)
                settings.DefaultSourceLanguage = SelectedSourceLanguage.Code;
            if (SelectedTargetLanguage != null)
                settings.DefaultTargetLanguage = SelectedTargetLanguage.Code;

            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save language preferences in TranslatorViewModel.", ex);
        }
    }

    private async Task RestartSessionForLanguageChangeAsync()
    {
        try
        {
            await _microphoneService.StopCaptureAsync();
            await _speechService.DisconnectAsync();

            var deepgramKey = await _credentialStore.GetApiKeyAsync("Deepgram");
            if (string.IsNullOrWhiteSpace(deepgramKey))
                return;

            var srcLang = SelectedSourceLanguage?.Code ?? "hi";
            var options = new SpeechToTextOptions
            {
                SourceLanguage = (srcLang == "auto") ? "en" : srcLang,
                Model = "nova-2",
                SampleRate = 16000,
                Channels = 1,
                InterimResults = true,
                SmartFormat = true,
                EndpointingMs = 300
            };

            await _speechService.ConnectAsync(options, deepgramKey);
            if (SelectedDevice != null)
            {
                await _microphoneService.StartCaptureAsync(SelectedDevice);
            }
            StatusMessage = $"Listening live on {SelectedDevice?.Name}... ({SelectedSourceLanguage?.DisplayName} → {SelectedTargetLanguage?.DisplayName})";
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to reconnect Deepgram for source language switch.", ex);
        }
    }

    private void SwapLanguages()
    {
        if (SelectedSourceLanguage == null || SelectedTargetLanguage == null)
            return;

        if (SelectedSourceLanguage.Code == "auto")
            return; // Cannot set target to auto detect

        var oldSource = SelectedSourceLanguage;
        var oldTarget = SelectedTargetLanguage;

        var newSource = AvailableSourceLanguages.FirstOrDefault(l => l.Code.Equals(oldTarget.Code, StringComparison.OrdinalIgnoreCase));
        var newTarget = AvailableTargetLanguages.FirstOrDefault(l => l.Code.Equals(oldSource.Code, StringComparison.OrdinalIgnoreCase));

        if (newSource != null && newTarget != null)
        {
            SelectedSourceLanguage = newSource;
            SelectedTargetLanguage = newTarget;
        }
    }

    public async Task LoadDevicesAsync()
    {
        try
        {
            var devices = await _microphoneService.GetAudioDevicesAsync();
            AvailableDevices.Clear();
            foreach (var dev in devices)
            {
                AvailableDevices.Add(dev);
            }

            if (AvailableDevices.Count == 0)
            {
                SelectedDevice = null;
                DeepgramStatusText = "No Microphone";
                StatusMessage = "No microphone devices found on this system.";
            }
            else
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IsDefault) ?? AvailableDevices.First();
                StatusMessage = $"Ready to capture from {SelectedDevice.Name}";
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error loading audio devices.", ex);
            StatusMessage = "Failed to enumerate audio devices.";
        }
    }

    private bool _isStartedViaHotkey;

    private async Task StartSessionAsync(bool fromHotkey = false)
    {
        _isStartedViaHotkey = fromHotkey;

        if (SelectedDevice == null)
        {
            StatusMessage = "Please select a microphone.";
            return;
        }

        // Retrieve Deepgram API key from secure DPAPI store
        var deepgramKey = await _credentialStore.GetApiKeyAsync("Deepgram");
        if (string.IsNullOrWhiteSpace(deepgramKey))
        {
            DeepgramStatusText = "Key Missing";
            StatusMessage = "Deepgram API key not found. Please enter and test your key in Settings.";
            return;
        }

        // Check translation provider pool status
        var poolEntries = await _translationPoolService.GetPoolEntriesAsync();
        var enabledEntries = poolEntries.Where(e => e.IsEnabled).ToList();
        if (enabledEntries.Count == 0)
        {
            StatusMessage = "Warning: No translation providers enabled in Settings pool. Transcripts will not be translated.";
        }
        else
        {
            var topProvider = enabledEntries.First();
            StatusMessage = $"Starting session with translation pool (Primary: {topProvider.Label})...";
        }

        try
        {
            StatusMessage = "Connecting to Deepgram WebSocket...";

            var srcLang = SelectedSourceLanguage?.Code ?? "hi";
            var options = new SpeechToTextOptions
            {
                SourceLanguage = (srcLang == "auto") ? "en" : srcLang, // If auto, fallback to en or Deepgram detect
                Model = "nova-2",
                SampleRate = 16000,
                Channels = 1,
                InterimResults = true,
                SmartFormat = true,
                EndpointingMs = 300
            };

            await _speechService.ConnectAsync(options, deepgramKey);
            await _microphoneService.StartCaptureAsync(SelectedDevice);

            IsListening = true;
            InterimText = string.Empty;
            _accumulatedFinalText = string.Empty;
            StatusMessage = $"Listening live on {SelectedDevice.Name}... ({SelectedSourceLanguage?.DisplayName} → {SelectedTargetLanguage?.DisplayName})";
        }
        catch (UnauthorizedAccessException uex)
        {
            IsListening = false;
            DeepgramStatusText = "Permission Denied";
            StatusMessage = uex.Message;
        }
        catch (Exception ex)
        {
            IsListening = false;
            DeepgramStatusText = "Failed";
            StatusMessage = ex.Message;
            await _speechService.DisconnectAsync();
            await _microphoneService.StopCaptureAsync();
        }
    }

    private async Task StopSessionAsync()
    {
        _isStartedViaHotkey = false;

        try
        {
            await _microphoneService.StopCaptureAsync();

            // Flush pending speech with Deepgram Finalize before closing connection
            if (_speechService.State == ConnectionState.Connected)
            {
                await _speechService.FinalizeAsync();
                await Task.Delay(150);
            }

            await _speechService.DisconnectAsync();

            // Commit any trailing finalized text buffer
            if (!string.IsNullOrWhiteSpace(_accumulatedFinalText))
            {
                CommitFinalTranscript(_accumulatedFinalText.Trim(), 1.0);
                _accumulatedFinalText = string.Empty;
            }
        }
        finally
        {
            IsListening = false;
            CurrentLevel = 0.0f;
            LevelSegments = 0;
            InterimText = string.Empty;
            _ttsPlaybackService?.StopAllPlayback();
            StatusMessage = "Translation session stopped.";
        }
    }

    private async void OnHotkeyPressed()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        try
        {
            await dispatcher.InvokeAsync(async () =>
            {
                if (!IsListening && SelectedDevice != null)
                {
                    Logger.Info("Push-to-Talk hotkey activated: starting capture.");
                    await StartSessionAsync(fromHotkey: true);
                }
            }).Task.Unwrap();
        }
        catch (Exception ex)
        {
            Logger.Error("Exception during hotkey StartSessionAsync execution.", ex);
            StatusMessage = $"Capture failed: {ex.Message}";
            DeepgramStatusText = "Failed";
            IsListening = false;
        }
    }

    private async void OnHotkeyReleased()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        try
        {
            await dispatcher.InvokeAsync(async () =>
            {
                // Only stop if the session was explicitly started by holding down the Push-to-Talk hotkey
                if (IsListening && _isStartedViaHotkey)
                {
                    Logger.Info("Push-to-Talk hotkey released: stopping capture & finalizing.");
                    _isStartedViaHotkey = false;
                    await StopSessionAsync();
                }
            }).Task.Unwrap();
        }
        catch (Exception ex)
        {
            Logger.Error("Exception during hotkey StopSessionAsync execution.", ex);
            StatusMessage = $"Stop failed: {ex.Message}";
            IsListening = false;
        }
    }

    private void OnAudioDataAvailable(object? sender, AudioCapturedEventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            if (!IsListening) return;
            CurrentLevel = e.PeakLevel;
            LevelSegments = Math.Clamp((int)Math.Round(e.PeakLevel * 10), 0, 10);
        });

        // Phase 10 Feedback Loop Suppression:
        // When TTS audio is actively playing through output endpoints,
        // suppress forwarding captured microphone buffers to Deepgram STT to avoid
        // re-transcribing our own synthesized speech in continuous listening mode.
        if (_ttsPlaybackService != null && _ttsPlaybackService.IsPlaying)
        {
            return;
        }

        // Feed raw 16kHz mono PCM buffer to Deepgram streaming service
        if (IsListening && _speechService.State == ConnectionState.Connected)
        {
            _ = _speechService.SendAudioAsync(e.Buffer);
        }
    }

    private void OnInterimTranscriptReceived(object? sender, TranscriptReceivedEventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            if (!IsListening) return;

            // Display accumulated settled chunks alongside speculative in-progress words
            if (!string.IsNullOrWhiteSpace(_accumulatedFinalText))
            {
                InterimText = $"{_accumulatedFinalText} {e.Transcript}";
            }
            else
            {
                InterimText = e.Transcript;
            }
        });
    }

    private void OnFinalTranscriptReceived(object? sender, TranscriptReceivedEventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            if (!IsListening) return;

            if (string.IsNullOrWhiteSpace(e.Transcript))
                return;

            if (string.IsNullOrWhiteSpace(_accumulatedFinalText))
            {
                _accumulatedFinalText = e.Transcript;
            }
            else
            {
                _accumulatedFinalText += " " + e.Transcript;
            }

            // If speaker paused / completed utterance, commit to permanent history
            if (e.IsSpeechFinal)
            {
                CommitFinalTranscript(_accumulatedFinalText.Trim(), e.Confidence);
                _accumulatedFinalText = string.Empty;
                InterimText = string.Empty;
            }
            else
            {
                InterimText = _accumulatedFinalText;
            }
        });
    }

    private void CommitFinalTranscript(string transcript, double confidence)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return;

        var sourceLangCode = SelectedSourceLanguage?.Code ?? "auto";
        var targetLangCode = SelectedTargetLanguage?.Code ?? "en";

        var message = new TranslationMessage
        {
            Timestamp = DateTime.Now,
            Speaker = "You",
            OriginalText = transcript,
            TranslatedText = "Translating...",
            IsTranslating = true,
            IsTranslationFailed = false,
            SourceLanguage = sourceLangCode,
            TargetLanguage = targetLangCode,
            Confidence = confidence
        };

        TranscriptHistory.Insert(0, message);

        // Asynchronous translation task that updates the message in-place without blocking
        _ = TranslateMessageAsync(message);
    }

    private async Task TranslateMessageAsync(TranslationMessage message)
    {
        try
        {
            var poolResult = await _translationPoolService.TranslateAsync(
                message.OriginalText,
                message.SourceLanguage,
                message.TargetLanguage);

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (poolResult.IsSuccess)
                {
                    message.TranslatedText = poolResult.TranslatedText;
                    message.IsTranslating = false;
                    message.IsTranslationFailed = false;

                    // Trigger Voice Output if opt-in enabled
                    _ = HandleVoiceOutputAsync(poolResult.TranslatedText, message.TargetLanguage);
                }
                else
                {
                    message.TranslatedText = $"[Translation Failed: {poolResult.ErrorMessage}]";
                    message.IsTranslating = false;
                    message.IsTranslationFailed = true;
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected translation error for message.", ex);
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                message.TranslatedText = $"[Translation Error: {ex.Message}]";
                message.IsTranslating = false;
                message.IsTranslationFailed = true;
            });
        }
    }

    private async Task HandleVoiceOutputAsync(string translatedText, string targetLanguage)
    {
        if (_ttsService == null || _ttsPlaybackService == null || string.IsNullOrWhiteSpace(translatedText))
            return;

        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            if (!settings.IsVoiceOutputEnabled)
                return; // Strictly opt-in

            var isSupported = await _ttsService.IsLanguageSupportedAsync(targetLanguage);
            if (!isSupported)
            {
                // Unsupported target language (e.g. Hindi, Arabic, Russian) - do not fail or attempt fake speech
                return;
            }

            var options = new TtsOptions(
                Voice: settings.SelectedTtsVoice,
                Volume: settings.TtsVolume,
                Speed: settings.TtsSpeed);

            var audioBytes = await _ttsService.SynthesizeAsync(translatedText, targetLanguage, options);
            if (audioBytes != null && audioBytes.Length > 0)
            {
                _ttsPlaybackService.EnqueueAudio(audioBytes);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Voice synthesis failed for target text in '{targetLanguage}'.", ex);
        }
    }

    private void OnSpeechStateChanged(object? sender, ConnectionState state)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            DeepgramState = state;
            DeepgramStatusText = state switch
            {
                ConnectionState.Connected => "Connected to Deepgram",
                ConnectionState.Connecting => "Connecting...",
                ConnectionState.Reconnecting => "Reconnecting to Deepgram...",
                ConnectionState.Failed => "Connection Failed",
                _ => "Disconnected"
            };

            if (state == ConnectionState.Failed && IsListening)
            {
                _ = StopSessionAsync();
            }
        });
    }

    private void OnSpeechErrorOccurred(object? sender, SpeechServiceErrorEventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            StatusMessage = e.Message;
        });
    }

    private void OnCaptureError(object? sender, CaptureErrorEventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            IsListening = false;
            CurrentLevel = 0.0f;
            LevelSegments = 0;
            StatusMessage = e.Message;
        });
    }
}
