using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
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

public class SettingsViewModel : ViewModelBase
{
    private readonly ISecureCredentialStore _credentialStore;
    private readonly IDeepgramValidator _deepgramValidator;
    private readonly IGoogleTranslateValidator _googleValidator;
    private readonly ILectoTranslateValidator _lectoValidator;
    private readonly ITranslationPoolService _translationPoolService;
    private readonly SettingsService _settingsService;
    private readonly IMicrophoneService _microphoneService;
    private readonly ArgosProcessManager _argosProcessManager;
    private readonly LocalArgosTranslationService _argosTranslationService;
    private readonly PiperProcessManager _piperProcessManager;
    private readonly DeepgramTtsService _deepgramTtsService;
    private readonly PiperTtsService _piperTtsService;
    private readonly EdgeTtsService _edgeTtsService;
    private readonly TtsPlaybackService _ttsPlaybackService;
    private readonly IVirtualAudioRoutingService _virtualAudioRoutingService;

    public ObservableCollection<TranslationProviderItemViewModel> TranslationProviders { get; } = new();

    private bool _isAddingProvider;
    private string _newProviderType = "GoogleTranslate";
    private string _newProviderLabel = string.Empty;
    private string _newProviderApiKey = string.Empty;
    private bool _isNewKeyVisible;
    private long? _newProviderSoftLimit = 500000;
    private string _addProviderStatusMessage = string.Empty;
    private bool _isAddProviderTesting;

    public bool IsAddingProvider
    {
        get => _isAddingProvider;
        set
        {
            if (SetProperty(ref _isAddingProvider, value))
            {
                OnPropertyChanged(nameof(IsNotAddingProvider));
            }
        }
    }

    public bool IsNotAddingProvider => !IsAddingProvider;

    public string NewProviderType
    {
        get => _newProviderType;
        set
        {
            if (SetProperty(ref _newProviderType, value))
            {
                OnPropertyChanged(nameof(IsNewProviderRequiresKey));
                OnPropertyChanged(nameof(DefaultPlaceholderLabel));
                OnPropertyChanged(nameof(IsNewGoogleSelected));
                OnPropertyChanged(nameof(IsNewLectoSelected));
                OnPropertyChanged(nameof(IsNewArgosSelected));
                if (string.IsNullOrWhiteSpace(NewProviderLabel) || IsDefaultLabel(NewProviderLabel))
                {
                    NewProviderLabel = DefaultPlaceholderLabel;
                }
            }
        }
    }

    public bool IsNewGoogleSelected
    {
        get => NewProviderType.Equals("GoogleTranslate", StringComparison.OrdinalIgnoreCase);
        set { if (value) NewProviderType = "GoogleTranslate"; }
    }

    public bool IsNewLectoSelected
    {
        get => NewProviderType.Equals("Lecto", StringComparison.OrdinalIgnoreCase);
        set { if (value) NewProviderType = "Lecto"; }
    }

    public bool IsNewArgosSelected
    {
        get => NewProviderType.Equals("ArgosTranslate", StringComparison.OrdinalIgnoreCase);
        set { if (value) NewProviderType = "ArgosTranslate"; }
    }

    public bool IsNewProviderRequiresKey => !NewProviderType.Equals("ArgosTranslate", StringComparison.OrdinalIgnoreCase);

    public string DefaultPlaceholderLabel => NewProviderType switch
    {
        "GoogleTranslate" => "My Google Cloud Key",
        "Lecto" => "My Lecto RapidAPI Key",
        "ArgosTranslate" => "Offline Engine (Argos)",
        _ => "Translation Provider"
    };

    private static bool IsDefaultLabel(string label) =>
        label is "My Google Cloud Key" or "My Lecto RapidAPI Key" or "Offline Engine (Argos)" or "Translation Provider";

    public string NewProviderLabel
    {
        get => _newProviderLabel;
        set => SetProperty(ref _newProviderLabel, value);
    }

    public string NewProviderApiKey
    {
        get => _newProviderApiKey;
        set => SetProperty(ref _newProviderApiKey, value);
    }

    public bool IsNewKeyVisible
    {
        get => _isNewKeyVisible;
        set => SetProperty(ref _isNewKeyVisible, value);
    }

    public long? NewProviderSoftLimit
    {
        get => _newProviderSoftLimit;
        set => SetProperty(ref _newProviderSoftLimit, value);
    }

    public string AddProviderStatusMessage
    {
        get => _addProviderStatusMessage;
        set
        {
            if (SetProperty(ref _addProviderStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasAddProviderStatusMessage));
            }
        }
    }

    public bool HasAddProviderStatusMessage => !string.IsNullOrWhiteSpace(AddProviderStatusMessage);

    public bool IsAddProviderTesting
    {
        get => _isAddProviderTesting;
        set => SetProperty(ref _isAddProviderTesting, value);
    }

    public ICommand ShowAddProviderCommand { get; }
    public ICommand CancelAddProviderCommand { get; }
    public ICommand ToggleNewKeyVisibilityCommand { get; }
    public ICommand TestAndSaveNewProviderCommand { get; }

    private AudioDevice? _selectedMicrophone;
    private Language _selectedSourceLanguage;
    private Language _selectedTargetLanguage;
    private string _selectedTranslationProvider = "GoogleTranslate";

    // Voice Output (TTS) state
    private bool _isVoiceOutputEnabled = false;
    private string _selectedTtsProvider = "Deepgram";
    private TtsVoiceOption? _selectedVoice;
    private double _ttsVolume = 100.0;
    private double _ttsSpeed = 1.0;
    private string _ttsLanguageStatusMessage = string.Empty;
    private bool _canSwitchToPiper;
    private bool _isTestingVoice;

    // Phase 8: Push-to-Talk Hotkey state
    private readonly GlobalHotkeyService _hotkeyService;
    private string _hotkeyDisplayName = "F9";
    private bool _isPushToTalkMode = true;
    private bool _isToggleMode = false;
    private bool _isRecordingHotkey = false;
    private string _hotkeyStatusMessage = "Ready";

    public event Action<int, string, HotkeyMode>? HotkeyChanged;

    public string HotkeyDisplayName
    {
        get => _hotkeyDisplayName;
        set => SetProperty(ref _hotkeyDisplayName, value);
    }

    public bool IsPushToTalkMode
    {
        get => _isPushToTalkMode;
        set
        {
            if (SetProperty(ref _isPushToTalkMode, value) && value)
            {
                IsToggleMode = false;
                _hotkeyService.Mode = HotkeyMode.PushToTalk;
                _ = SaveHotkeyPreferencesAsync();
                HotkeyChanged?.Invoke(_hotkeyService.VirtualKey, _hotkeyService.KeyDisplayName, HotkeyMode.PushToTalk);
            }
        }
    }

    public bool IsToggleMode
    {
        get => _isToggleMode;
        set
        {
            if (SetProperty(ref _isToggleMode, value) && value)
            {
                IsPushToTalkMode = false;
                _hotkeyService.Mode = HotkeyMode.Toggle;
                _ = SaveHotkeyPreferencesAsync();
                HotkeyChanged?.Invoke(_hotkeyService.VirtualKey, _hotkeyService.KeyDisplayName, HotkeyMode.Toggle);
            }
        }
    }

    public bool IsRecordingHotkey
    {
        get => _isRecordingHotkey;
        set => SetProperty(ref _isRecordingHotkey, value);
    }

    public string HotkeyStatusMessage
    {
        get => _hotkeyStatusMessage;
        set => SetProperty(ref _hotkeyStatusMessage, value);
    }

    public ProviderCredentialViewModel DeepgramCredentialVM { get; }
    public ProviderCredentialViewModel GoogleCredentialVM { get; }

    public ObservableCollection<AudioDevice> AvailableMicrophones { get; } = new();
    public ObservableCollection<Language> AvailableSourceLanguages { get; } = new();
    public ObservableCollection<Language> AvailableTargetLanguages { get; } = new();
    public ObservableCollection<ArgosPackageItem> ArgosPackages { get; } = new();
    public ObservableCollection<PiperVoicePackage> PiperPackages { get; } = new();
    public ObservableCollection<TtsVoiceOption> AvailableVoices { get; } = new();

    public event Action<string>? TranslationProviderChanged;
    public event Action<string>? TtsProviderChanged;

    public string SelectedTranslationProvider
    {
        get => _selectedTranslationProvider;
        set
        {
            if (SetProperty(ref _selectedTranslationProvider, value))
            {
                OnPropertyChanged(nameof(IsGoogleSelected));
                OnPropertyChanged(nameof(IsArgosSelected));
                _ = SaveProviderPreferenceAsync();
                TranslationProviderChanged?.Invoke(value);

                if (IsArgosSelected)
                {
                    _ = EnsureArgosEngineStartedAsync();
                }
            }
        }
    }

    public bool IsGoogleSelected
    {
        get => SelectedTranslationProvider.Equals("GoogleTranslate", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTranslationProvider = "GoogleTranslate";
            }
        }
    }

    public bool IsArgosSelected
    {
        get => SelectedTranslationProvider.Equals("ArgosTranslate", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTranslationProvider = "ArgosTranslate";
            }
        }
    }

    public ArgosProcessState ArgosEngineState => _argosProcessManager.State;
    public string ArgosEngineStatusMessage => _argosProcessManager.StatusMessage;

    public PiperProcessState PiperEngineState => _piperProcessManager.State;
    public string PiperEngineStatusMessage => _piperProcessManager.StatusMessage;

    public bool IsVoiceOutputEnabled
    {
        get => _isVoiceOutputEnabled;
        set
        {
            if (SetProperty(ref _isVoiceOutputEnabled, value))
            {
                _ = SaveTtsPreferencesAsync();
                if (value && IsPiperTtsSelected)
                {
                    _ = EnsurePiperEngineStartedAsync();
                }
                else if (!value)
                {
                    _ttsPlaybackService.StopAllPlayback();
                }
            }
        }
    }

    public string SelectedTtsProvider
    {
        get => _selectedTtsProvider;
        set
        {
            if (SetProperty(ref _selectedTtsProvider, value))
            {
                OnPropertyChanged(nameof(IsDeepgramTtsSelected));
                OnPropertyChanged(nameof(IsPiperTtsSelected));
                OnPropertyChanged(nameof(IsEdgeTtsSelected));
                _ = SaveTtsPreferencesAsync();
                TtsProviderChanged?.Invoke(value);
                UpdateVoiceList();
                UpdateLanguageCompatibility();

                if ((IsPiperTtsSelected || IsEdgeTtsSelected) && IsVoiceOutputEnabled)
                {
                    _ = EnsurePiperEngineStartedAsync();
                }
            }
        }
    }

    public bool IsDeepgramTtsSelected
    {
        get => SelectedTtsProvider.Equals("Deepgram", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTtsProvider = "Deepgram";
            }
        }
    }

    public bool IsPiperTtsSelected
    {
        get => SelectedTtsProvider.Equals("Piper", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTtsProvider = "Piper";
            }
        }
    }

    public bool IsEdgeTtsSelected
    {
        get => SelectedTtsProvider.Equals("EdgeTts", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTtsProvider = "EdgeTts";
            }
        }
    }

    public TtsVoiceOption? SelectedVoice
    {
        get => _selectedVoice;
        set
        {
            if (SetProperty(ref _selectedVoice, value) && value != null)
            {
                _ = SaveTtsPreferencesAsync();
            }
        }
    }

    public double TtsVolume
    {
        get => _ttsVolume;
        set
        {
            if (SetProperty(ref _ttsVolume, Math.Clamp(value, 0.0, 200.0)))
            {
                _ttsPlaybackService.Volume = (float)(_ttsVolume / 100.0);
                _ = SaveTtsPreferencesAsync();
            }
        }
    }

    public double TtsSpeed
    {
        get => _ttsSpeed;
        set
        {
            if (SetProperty(ref _ttsSpeed, Math.Round(Math.Clamp(value, 0.5, 2.0), 1)))
            {
                _ = SaveTtsPreferencesAsync();
            }
        }
    }

    public string TtsLanguageStatusMessage
    {
        get => _ttsLanguageStatusMessage;
        set => SetProperty(ref _ttsLanguageStatusMessage, value);
    }

    public bool CanSwitchToPiper
    {
        get => _canSwitchToPiper;
        set => SetProperty(ref _canSwitchToPiper, value);
    }

    public bool IsTestingVoice
    {
        get => _isTestingVoice;
        set => SetProperty(ref _isTestingVoice, value);
    }

    public AudioDevice? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set
        {
            if (SetProperty(ref _selectedMicrophone, value) && value != null)
            {
                _ = SaveMicrophonePreferenceAsync(value.Id);
            }
        }
    }

    public Language SelectedSourceLanguage
    {
        get => _selectedSourceLanguage;
        set
        {
            if (SetProperty(ref _selectedSourceLanguage, value) && value != null)
            {
                _ = SaveLanguagePreferenceAsync();
            }
        }
    }

    public Language SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set
        {
            if (SetProperty(ref _selectedTargetLanguage, value) && value != null)
            {
                _ = SaveLanguagePreferenceAsync();
                UpdateVoiceList();
                UpdateLanguageCompatibility();
            }
        }
    }

    public ICommand RefreshMicrophonesCommand { get; }
    public ICommand StartArgosEngineCommand { get; }
    public ICommand StopArgosEngineCommand { get; }
    public ICommand RestartArgosEngineCommand { get; }
    public ICommand SelectGoogleCommand { get; }
    public ICommand SelectArgosCommand { get; }

    // TTS Commands
    public ICommand SelectDeepgramTtsCommand { get; }
    public ICommand SelectPiperTtsCommand { get; }
    public ICommand SelectEdgeTtsCommand { get; }
    public ICommand SwitchToPiperCommand { get; }
    public ICommand TestVoiceCommand { get; }
    public ICommand StartPiperEngineCommand { get; }
    public ICommand StopPiperEngineCommand { get; }
    public ICommand RestartPiperEngineCommand { get; }

    // Hotkey Commands
    public ICommand RecordHotkeyCommand { get; }
    public ICommand CancelRecordHotkeyCommand { get; }
    public ICommand SelectPushToTalkCommand { get; }
    public ICommand SelectToggleModeCommand { get; }

    // Phase 10: Virtual Audio Routing
    private bool _playTtsThroughSpeakers = true;
    private bool _routeTtsToVirtualDevice = false;
    private AudioDevice? _selectedVirtualDevice;
    private bool _isVirtualDeviceDetected;
    private string _virtualDeviceStatusText = "Click 'Detect Virtual Device' or install VB-CABLE.";
    private bool _isSetupGuideVisible;

    public ObservableCollection<AudioDevice> AvailableVirtualDevices { get; } = new();

    public bool PlayTtsThroughSpeakers
    {
        get => _playTtsThroughSpeakers;
        set
        {
            if (SetProperty(ref _playTtsThroughSpeakers, value))
            {
                _ttsPlaybackService.PlayThroughSpeakers = value;
                _ = SaveRoutingPreferencesAsync();
            }
        }
    }

    public bool RouteTtsToVirtualDevice
    {
        get => _routeTtsToVirtualDevice;
        set
        {
            if (SetProperty(ref _routeTtsToVirtualDevice, value))
            {
                _ttsPlaybackService.RouteToVirtualDevice = value;
                _ = SaveRoutingPreferencesAsync();
            }
        }
    }

    public AudioDevice? SelectedVirtualDevice
    {
        get => _selectedVirtualDevice;
        set
        {
            if (SetProperty(ref _selectedVirtualDevice, value))
            {
                _ttsPlaybackService.VirtualDeviceId = value?.Id;
                _ttsPlaybackService.VirtualDeviceName = value?.Name;
                _ = SaveRoutingPreferencesAsync();
            }
        }
    }

    public bool IsVirtualDeviceDetected
    {
        get => _isVirtualDeviceDetected;
        set => SetProperty(ref _isVirtualDeviceDetected, value);
    }

    public string VirtualDeviceStatusText
    {
        get => _virtualDeviceStatusText;
        set => SetProperty(ref _virtualDeviceStatusText, value);
    }

    public bool IsSetupGuideVisible
    {
        get => _isSetupGuideVisible;
        set => SetProperty(ref _isSetupGuideVisible, value);
    }

    // Phase 10 Routing Commands
    public ICommand DetectVirtualDeviceCommand { get; }
    public ICommand ToggleSetupGuideCommand { get; }
    public ICommand OpenVbCableDownloadCommand { get; }

    public SettingsViewModel(
        ISecureCredentialStore? credentialStore = null,
        IDeepgramValidator? deepgramValidator = null,
        IGoogleTranslateValidator? googleValidator = null,
        SettingsService? settingsService = null,
        IMicrophoneService? microphoneService = null,
        ArgosProcessManager? argosProcessManager = null,
        LocalArgosTranslationService? argosTranslationService = null,
        PiperProcessManager? piperProcessManager = null,
        DeepgramTtsService? deepgramTtsService = null,
        PiperTtsService? piperTtsService = null,
        EdgeTtsService? edgeTtsService = null,
        TtsPlaybackService? ttsPlaybackService = null,
        GlobalHotkeyService? hotkeyService = null,
        ITranslationPoolService? translationPoolService = null,
        ILectoTranslateValidator? lectoValidator = null,
        IVirtualAudioRoutingService? virtualAudioRoutingService = null)
    {
        Title = "Settings";
        _credentialStore = credentialStore ?? new SecureCredentialStore();
        _deepgramValidator = deepgramValidator ?? new DeepgramValidator();
        _googleValidator = googleValidator ?? new GoogleTranslateValidator();
        _lectoValidator = lectoValidator ?? new LectoTranslateValidator();
        _settingsService = settingsService ?? new SettingsService();
        _microphoneService = microphoneService ?? new MicrophoneService();
        _virtualAudioRoutingService = virtualAudioRoutingService ?? new VirtualAudioRoutingService();
        _argosProcessManager = argosProcessManager ?? new ArgosProcessManager();
        _argosTranslationService = argosTranslationService ?? new LocalArgosTranslationService(_argosProcessManager);
        _translationPoolService = translationPoolService ?? new TranslationPoolService(
            _settingsService,
            _credentialStore,
            new TranslationService(_credentialStore),
            new LectoTranslationService(_credentialStore),
            _argosTranslationService);
        _piperProcessManager = piperProcessManager ?? new PiperProcessManager();
        _deepgramTtsService = deepgramTtsService ?? new DeepgramTtsService(_credentialStore);
        _piperTtsService = piperTtsService ?? new PiperTtsService(_piperProcessManager);
        _edgeTtsService = edgeTtsService ?? new EdgeTtsService(_piperProcessManager);
        _ttsPlaybackService = ttsPlaybackService ?? new TtsPlaybackService();
        _hotkeyService = hotkeyService ?? new GlobalHotkeyService();

        _argosProcessManager.StateChanged += (state, msg) =>
        {
            OnPropertyChanged(nameof(ArgosEngineState));
            OnPropertyChanged(nameof(ArgosEngineStatusMessage));
            if (state == ArgosProcessState.Running)
            {
                _ = RefreshInstalledPackagesAsync();
            }
        };

        _piperProcessManager.StateChanged += (state, msg) =>
        {
            OnPropertyChanged(nameof(PiperEngineState));
            OnPropertyChanged(nameof(PiperEngineStatusMessage));
            UpdateLanguageCompatibility();
        };

        // Credential sub-viewmodels
        DeepgramCredentialVM = new ProviderCredentialViewModel(
            _credentialStore,
            key => _deepgramValidator.ValidateApiKeyAsync(key),
            "Deepgram",
            "SPEECH RECOGNITION (STT) & VOICE OUTPUT (TTS)",
            "Deepgram API Key",
            "Protected using Windows DPAPI and stored locally in %LOCALAPPDATA%\\GamingLiveTranslator\\secrets.dat.");

        GoogleCredentialVM = new ProviderCredentialViewModel(
            _credentialStore,
            key => _googleValidator.ValidateApiKeyAsync(key),
            "GoogleTranslate",
            "TRANSLATION ENGINE (GOOGLE CLOUD)",
            "Google Cloud Translation API Key",
            "Requires enabling 'Cloud Translation API' in Google Cloud Console. Stored via Windows DPAPI.");

        // Languages
        foreach (var lang in Language.GetInitialLanguages(includeAutoDetect: true))
            AvailableSourceLanguages.Add(lang);

        foreach (var lang in Language.GetInitialLanguages(includeAutoDetect: false))
            AvailableTargetLanguages.Add(lang);

        _selectedSourceLanguage = AvailableSourceLanguages.FirstOrDefault(l => l.Code == "hi") ?? AvailableSourceLanguages.First();
        _selectedTargetLanguage = AvailableTargetLanguages.FirstOrDefault(l => l.Code == "en") ?? AvailableTargetLanguages.First();

        SelectGoogleCommand = new RelayCommand(() => SelectedTranslationProvider = "GoogleTranslate");
        SelectArgosCommand = new RelayCommand(() => SelectedTranslationProvider = "ArgosTranslate");
        RefreshMicrophonesCommand = new RelayCommand(async () => await LoadMicrophonesAsync());
        StartArgosEngineCommand = new RelayCommand(async () => await _argosProcessManager.StartAsync());
        StopArgosEngineCommand = new RelayCommand(async () => await _argosProcessManager.StopAsync());
        RestartArgosEngineCommand = new RelayCommand(async () => await _argosProcessManager.RestartAsync());

        // Voice output commands
        SelectDeepgramTtsCommand = new RelayCommand(() => SelectedTtsProvider = "Deepgram");
        SelectPiperTtsCommand = new RelayCommand(() => SelectedTtsProvider = "Piper");
        SelectEdgeTtsCommand = new RelayCommand(() => SelectedTtsProvider = "EdgeTts");
        SwitchToPiperCommand = new RelayCommand(() => SelectedTtsProvider = "Piper");
        TestVoiceCommand = new RelayCommand(async () => await ExecuteTestVoiceAsync(), () => !IsTestingVoice);
        StartPiperEngineCommand = new RelayCommand(async () => await _piperProcessManager.StartAsync());
        StopPiperEngineCommand = new RelayCommand(async () => await _piperProcessManager.StopAsync());
        RestartPiperEngineCommand = new RelayCommand(async () => await _piperProcessManager.RestartAsync());

        // Hotkey commands
        RecordHotkeyCommand = new RelayCommand(StartRecordingHotkey, () => !IsRecordingHotkey);
        CancelRecordHotkeyCommand = new RelayCommand(CancelRecordingHotkey, () => IsRecordingHotkey);
        SelectPushToTalkCommand = new RelayCommand(() => IsPushToTalkMode = true);
        SelectToggleModeCommand = new RelayCommand(() => IsToggleMode = true);

        // Translation Provider Pool commands
        ShowAddProviderCommand = new RelayCommand(ShowAddProvider);
        CancelAddProviderCommand = new RelayCommand(CancelAddProvider);
        ToggleNewKeyVisibilityCommand = new RelayCommand(() => IsNewKeyVisible = !IsNewKeyVisible);
        TestAndSaveNewProviderCommand = new RelayCommand(async () => await TestAndSaveNewProviderAsync(), () => !IsAddProviderTesting);

        // Phase 10: Virtual Audio Routing commands
        DetectVirtualDeviceCommand = new RelayCommand(async () => await DetectVirtualDeviceAsync());
        ToggleSetupGuideCommand = new RelayCommand(() => IsSetupGuideVisible = !IsSetupGuideVisible);
        OpenVbCableDownloadCommand = new RelayCommand(OpenVbCableDownloadPage);

        InitializeArgosPackages();
        InitializePiperPackages();
        _ = InitializeSettingsAsync();
    }

    private void InitializeArgosPackages()
    {
        ArgosPackages.Clear();
        var packages = new List<ArgosPackageItem>
        {
            new ArgosPackageItem("English ↔ Hindi", "hi", "en", "~85 MB"),
            new ArgosPackageItem("English ↔ Spanish", "es", "en", "~45 MB"),
            new ArgosPackageItem("English ↔ French", "fr", "en", "~45 MB"),
            new ArgosPackageItem("English ↔ German", "de", "en", "~45 MB"),
            new ArgosPackageItem("English ↔ Japanese", "ja", "en", "~85 MB"),
            new ArgosPackageItem("English ↔ Chinese", "zh", "en", "~95 MB"),
            new ArgosPackageItem("English ↔ Russian", "ru", "en", "~75 MB"),
            new ArgosPackageItem("English ↔ Arabic", "ar", "en", "~80 MB"),
            new ArgosPackageItem("English ↔ Portuguese", "pt", "en", "~45 MB"),
            new ArgosPackageItem("English ↔ Korean", "ko", "en", "~80 MB")
        };

        foreach (var pkg in packages)
        {
            pkg.DownloadCommand = new RelayCommand(async () => await DownloadPackageAsync(pkg), () => !pkg.IsDownloading);
            ArgosPackages.Add(pkg);
        }
    }

    private void InitializePiperPackages()
    {
        PiperPackages.Clear();

        // High quality, permissive open license with attribution (Tsukuyomi-chan Corpus)
        var packages = new List<PiperVoicePackage>
        {
            new PiperVoicePackage(
                id: "tsukuyomi-chan-6lang-fp16",
                displayName: "Multilingual 6-Language (Chinese / English / Spanish / Japanese - Fast CPU)",
                languageCode: "zh",
                approxSize: "~38 MB",
                modelFileName: "tsukuyomi-chan-6lang-fp16.onnx",
                configFileName: "tsukuyomi-chan-6lang-fp16.onnx.json",
                license: "Tsukuyomi-chan Corpus Terms (Attribution Required)",
                modelUrl: "https://huggingface.co/ayousanz/piper-plus-tsukuyomi-chan/resolve/main/tsukuyomi-chan-6lang-fp16.onnx",
                configUrl: "https://huggingface.co/ayousanz/piper-plus-tsukuyomi-chan/resolve/main/config.json"
            )
        };

        foreach (var pkg in packages)
        {
            pkg.DownloadCommand = new RelayCommand(async () => await DownloadPiperPackageAsync(pkg), () => !pkg.IsDownloading);
            PiperPackages.Add(pkg);
        }

        RefreshInstalledPiperPackages();
    }

    public void RefreshInstalledPiperPackages()
    {
        try
        {
            var modelsDir = PiperProcessManager.ResolveModelsDir();
            foreach (var pkg in PiperPackages)
            {
                var modelPath = Path.Combine(modelsDir, pkg.ModelFileName);
                if (File.Exists(modelPath) && new FileInfo(modelPath).Length > 1000)
                {
                    pkg.IsInstalled = true;
                    pkg.StatusText = "Installed (Ready)";
                }
                else
                {
                    pkg.IsInstalled = false;
                    pkg.StatusText = "Not installed";
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error checking installed Piper models.", ex);
        }
    }

    private async Task DownloadPiperPackageAsync(PiperVoicePackage pkg)
    {
        pkg.IsDownloading = true;
        pkg.StatusText = "Downloading voice model...";

        try
        {
            var modelsDir = PiperProcessManager.ResolveModelsDir();
            var modelDest = Path.Combine(modelsDir, pkg.ModelFileName);
            var configDest = Path.Combine(modelsDir, pkg.ConfigFileName);

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            // 1. Download ONNX model
            var modelBytes = await client.GetByteArrayAsync(pkg.ModelUrl);
            await File.WriteAllBytesAsync(modelDest, modelBytes);

            // 2. Download ONNX JSON config
            try
            {
                var configBytes = await client.GetByteArrayAsync(pkg.ConfigUrl);
                await File.WriteAllBytesAsync(configDest, configBytes);
            }
            catch (Exception cex)
            {
                Logger.Error($"Could not download config for {pkg.DisplayName}, using default.", cex);
            }

            pkg.IsInstalled = true;
            pkg.StatusText = "Installed (Ready)";
            Logger.Info($"Successfully installed Piper voice model: {pkg.DisplayName}");

            UpdateVoiceList();
            UpdateLanguageCompatibility();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to download Piper voice model {pkg.DisplayName}.", ex);
            pkg.StatusText = $"Download failed: {ex.Message}";
        }
        finally
        {
            pkg.IsDownloading = false;
        }
    }

    private void UpdateVoiceList()
    {
        AvailableVoices.Clear();
        var targetCode = SelectedTargetLanguage?.Code ?? "en";

        if (IsEdgeTtsSelected)
        {
            switch (targetCode.ToLowerInvariant())
            {
                case "hi":
                    AvailableVoices.Add(new TtsVoiceOption("hi-IN-SwaraNeural", "Swara (Hindi - Natural Female)", "hi", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("hi-IN-MadhurNeural", "Madhur (Hindi - Conversational Male)", "hi", "Edge TTS", "Male"));
                    break;
                case "zh":
                    AvailableVoices.Add(new TtsVoiceOption("zh-CN-XiaoxiaoNeural", "Xiaoxiao (Mandarin - Female Warm)", "zh", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("zh-CN-YunxiNeural", "Yunxi (Mandarin - Male Lively)", "zh", "Edge TTS", "Male"));
                    AvailableVoices.Add(new TtsVoiceOption("zh-CN-YunjianNeural", "Yunjian (Mandarin - Male Calm)", "zh", "Edge TTS", "Male"));
                    AvailableVoices.Add(new TtsVoiceOption("zh-CN-XiaoyiNeural", "Xiaoyi (Mandarin - Female Sweet)", "zh", "Edge TTS", "Female"));
                    break;
                case "ja":
                    AvailableVoices.Add(new TtsVoiceOption("ja-JP-NanamiNeural", "Nanami (Japanese - Female)", "ja", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("ja-JP-KeitaNeural", "Keita (Japanese - Male)", "ja", "Edge TTS", "Male"));
                    break;
                case "ko":
                    AvailableVoices.Add(new TtsVoiceOption("ko-KR-SunHiNeural", "SunHi (Korean - Female)", "ko", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("ko-KR-InJoonNeural", "InJoon (Korean - Male)", "ko", "Edge TTS", "Male"));
                    break;
                case "es":
                    AvailableVoices.Add(new TtsVoiceOption("es-ES-ElviraNeural", "Elvira (Spanish - Female)", "es", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("es-ES-AlvaroNeural", "Alvaro (Spanish - Male)", "es", "Edge TTS", "Male"));
                    break;
                case "fr":
                    AvailableVoices.Add(new TtsVoiceOption("fr-FR-DeniseNeural", "Denise (French - Female)", "fr", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("fr-FR-HenriNeural", "Henri (French - Male)", "fr", "Edge TTS", "Male"));
                    break;
                case "de":
                    AvailableVoices.Add(new TtsVoiceOption("de-DE-KatjaNeural", "Katja (German - Female)", "de", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("de-DE-ConradNeural", "Conrad (German - Male)", "de", "Edge TTS", "Male"));
                    break;
                case "pt":
                    AvailableVoices.Add(new TtsVoiceOption("pt-BR-FranciscaNeural", "Francisca (Portuguese BR - Female)", "pt", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("pt-BR-AntonioNeural", "Antonio (Portuguese BR - Male)", "pt", "Edge TTS", "Male"));
                    break;
                case "ru":
                    AvailableVoices.Add(new TtsVoiceOption("ru-RU-SvetlanaNeural", "Svetlana (Russian - Female)", "ru", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("ru-RU-DmitryNeural", "Dmitry (Russian - Male)", "ru", "Edge TTS", "Male"));
                    break;
                case "ar":
                    AvailableVoices.Add(new TtsVoiceOption("ar-SA-ZariyahNeural", "Zariyah (Arabic - Female)", "ar", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("ar-SA-HamedNeural", "Hamed (Arabic - Male)", "ar", "Edge TTS", "Male"));
                    break;
                default:
                    AvailableVoices.Add(new TtsVoiceOption("en-US-JennyNeural", "Jenny (English US - Female)", "en", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("en-US-GuyNeural", "Guy (English US - Male)", "en", "Edge TTS", "Male"));
                    AvailableVoices.Add(new TtsVoiceOption("en-GB-SoniaNeural", "Sonia (English UK - Female)", "en-GB", "Edge TTS", "Female"));
                    AvailableVoices.Add(new TtsVoiceOption("en-GB-RyanNeural", "Ryan (English UK - Male)", "en-GB", "Edge TTS", "Male"));
                    break;
            }
        }
        else if (IsDeepgramTtsSelected)
        {
            if (targetCode.Equals("es", StringComparison.OrdinalIgnoreCase))
            {
                AvailableVoices.Add(new TtsVoiceOption("aura-2-celeste-es", "Celeste (Spanish - Natural Female)", "es", "Deepgram", "Female"));
                AvailableVoices.Add(new TtsVoiceOption("aura-2-nestor-es", "Nestor (Spanish - Natural Male)", "es", "Deepgram", "Male"));
                AvailableVoices.Add(new TtsVoiceOption("aura-2-estrella-es", "Estrella (Spanish - Expressive Female)", "es", "Deepgram", "Female"));
            }
            else
            {
                // Default English catalog (Deepgram Aura-2)
                AvailableVoices.Add(new TtsVoiceOption("aura-2-asteria-en", "Asteria (English - Conversational Female)", "en", "Deepgram", "Female"));
                AvailableVoices.Add(new TtsVoiceOption("aura-2-luna-en", "Luna (English - Soft / Warm Female)", "en", "Deepgram", "Female"));
                AvailableVoices.Add(new TtsVoiceOption("aura-2-orion-en", "Orion (English - Natural Male)", "en", "Deepgram", "Male"));
                AvailableVoices.Add(new TtsVoiceOption("aura-2-arcas-en", "Arcas (English - Authoritative Male)", "en", "Deepgram", "Male"));
                AvailableVoices.Add(new TtsVoiceOption("aura-2-perseus-en", "Perseus (English - Dynamic Male)", "en", "Deepgram", "Male"));
                AvailableVoices.Add(new TtsVoiceOption("aura-2-angus-en", "Angus (English - Irish Male)", "en", "Deepgram", "Male"));
                AvailableVoices.Add(new TtsVoiceOption("aura-2-zeus-en", "Zeus (English - Deep Male)", "en", "Deepgram", "Male"));
            }
        }
        else
        {
            // Piper voices: verified native multilingual Tsukuyomi-chan model
            AvailableVoices.Add(new TtsVoiceOption(
                "tsukuyomi-chan-6lang-fp16",
                "Tsukuyomi (Multilingual - Fast Neural)",
                targetCode,
                "Piper",
                "Female"));
        }

        SelectedVoice = AvailableVoices.FirstOrDefault();
    }

    private void UpdateLanguageCompatibility()
    {
        var targetCode = SelectedTargetLanguage?.Code ?? "en";
        var targetName = SelectedTargetLanguage?.DisplayName ?? "Selected language";

        if (IsEdgeTtsSelected)
        {
            var isEdgeSupported = targetCode.ToLowerInvariant() switch
            {
                "hi" or "zh" or "ja" or "ko" or "en" or "es" or "fr" or "de" or "pt" or "ru" or "ar" => true,
                _ => false
            };

            if (isEdgeSupported)
            {
                TtsLanguageStatusMessage = $"Edge TTS (Free, Online, Unofficial) is ready for {targetName}.";
            }
            else
            {
                TtsLanguageStatusMessage = $"Edge TTS does not support {targetName}.";
            }
            CanSwitchToPiper = false;
            return;
        }

        // Explicit unsupported list for both providers
        if (targetCode.Equals("hi", StringComparison.OrdinalIgnoreCase) ||
            targetCode.Equals("ar", StringComparison.OrdinalIgnoreCase) ||
            targetCode.Equals("ru", StringComparison.OrdinalIgnoreCase))
        {
            TtsLanguageStatusMessage = $"Voice output is not available for {targetName}. (Neither Deepgram nor Piper supports this language).";
            CanSwitchToPiper = false;
            return;
        }

        if (IsDeepgramTtsSelected)
        {
            if (targetCode.Equals("en", StringComparison.OrdinalIgnoreCase) || targetCode.Equals("es", StringComparison.OrdinalIgnoreCase))
            {
                TtsLanguageStatusMessage = $"Deepgram Aura-2 is ready for {targetName}.";
                CanSwitchToPiper = false;
            }
            else if (targetCode.Equals("zh", StringComparison.OrdinalIgnoreCase))
            {
                TtsLanguageStatusMessage = "Deepgram Aura does not support Chinese. Switch to Piper (Offline) for Chinese voice output.";
                CanSwitchToPiper = true;
            }
            else
            {
                TtsLanguageStatusMessage = $"Deepgram Aura does not support {targetName}. Only English and Spanish are supported.";
                CanSwitchToPiper = targetCode.Equals("ja", StringComparison.OrdinalIgnoreCase) || targetCode.Equals("fr", StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            // Piper selected
            var pkg = PiperPackages.FirstOrDefault(p => p.Id == "tsukuyomi-chan-6lang-fp16");
            if (pkg != null && pkg.IsInstalled)
            {
                TtsLanguageStatusMessage = $"Piper multilingual voice model is installed and ready for {targetName}.";
            }
            else
            {
                TtsLanguageStatusMessage = $"Piper voice model is not installed. Download the multilingual package below to enable voice output.";
            }
            CanSwitchToPiper = false;
        }
    }

    private async Task ExecuteTestVoiceAsync()
    {
        IsTestingVoice = true;
        try
        {
            var targetCode = SelectedTargetLanguage?.Code ?? "en";
            var text = targetCode.Equals("zh", StringComparison.OrdinalIgnoreCase)
                ? "你好，这是语音输出测试。"
                : (targetCode.Equals("es", StringComparison.OrdinalIgnoreCase)
                    ? "Hola, esta es una prueba de salida de voz."
                    : "Hello, this is a test of the gaming voice output system.");

            var options = new TtsOptions(
                Voice: SelectedVoice?.Id,
                Volume: _ttsVolume / 100.0,
                Speed: _ttsSpeed);

            ITextToSpeechService service = IsEdgeTtsSelected
                ? _edgeTtsService
                : (IsPiperTtsSelected ? _piperTtsService : _deepgramTtsService);

            var supported = await service.IsLanguageSupportedAsync(targetCode);
            if (!supported)
            {
                TtsLanguageStatusMessage = $"Cannot test: {service.ProviderName} does not support {SelectedTargetLanguage?.DisplayName}.";
                return;
            }

            var audioBytes = await service.SynthesizeAsync(text, targetCode, options);
            if (audioBytes != null && audioBytes.Length > 0)
            {
                _ttsPlaybackService.EnqueueAudio(audioBytes);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Test voice playback failed.", ex);
            TtsLanguageStatusMessage = $"Voice test failed: {ex.Message}";
        }
        finally
        {
            IsTestingVoice = false;
        }
    }

    public async Task RefreshInstalledPackagesAsync()
    {
        try
        {
            var installed = await _argosTranslationService.GetInstalledPackagesAsync();
            foreach (var item in ArgosPackages)
            {
                bool isInstalled = installed.Any(p =>
                    p.Equals($"{item.FromCode}_{item.ToCode}", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals($"{item.FromCode}->{item.ToCode}", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals($"{item.ToCode}_{item.FromCode}", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals($"{item.ToCode}->{item.FromCode}", StringComparison.OrdinalIgnoreCase));

                if (isInstalled)
                {
                    item.IsInstalled = true;
                    item.StatusText = "Installed (Ready)";
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to refresh installed Argos packages.", ex);
        }
    }

    private async Task DownloadPackageAsync(ArgosPackageItem item)
    {
        item.IsDownloading = true;
        item.StatusText = "Downloading model (1/2)...";

        try
        {
            var success1 = await _argosTranslationService.InstallPackageAsync(item.FromCode, item.ToCode);
            item.StatusText = "Downloading reverse model (2/2)...";
            var success2 = await _argosTranslationService.InstallPackageAsync(item.ToCode, item.FromCode);

            if (success1 || success2)
            {
                item.IsInstalled = true;
                item.StatusText = "Installed (Ready)";
                await RefreshInstalledPackagesAsync();
            }
            else
            {
                item.StatusText = "Download failed. Check internet.";
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error downloading package {item.DisplayName}", ex);
            item.StatusText = $"Download error: {ex.Message}";
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    private async Task EnsureArgosEngineStartedAsync()
    {
        if (!_argosProcessManager.IsRunning && _argosProcessManager.State != ArgosProcessState.Starting)
        {
            await _argosProcessManager.StartAsync();
        }
    }

    private async Task EnsurePiperEngineStartedAsync()
    {
        if (!_piperProcessManager.IsRunning && _piperProcessManager.State != PiperProcessState.Starting)
        {
            await _piperProcessManager.StartAsync();
        }
    }

    private async Task InitializeSettingsAsync()
    {
        await LoadMicrophonesAsync();
        await LoadLanguagePreferencesAsync();
        await LoadTranslationPoolAsync();

        var settings = await _settingsService.LoadSettingsAsync();
        if (!string.IsNullOrEmpty(settings.PreferredTranslationProvider))
        {
            _selectedTranslationProvider = settings.PreferredTranslationProvider;
            OnPropertyChanged(nameof(SelectedTranslationProvider));
            OnPropertyChanged(nameof(IsGoogleSelected));
            OnPropertyChanged(nameof(IsArgosSelected));

            if (IsArgosSelected)
            {
                _ = EnsureArgosEngineStartedAsync();
            }
        }

        _isVoiceOutputEnabled = settings.IsVoiceOutputEnabled;
        OnPropertyChanged(nameof(IsVoiceOutputEnabled));

        if (!string.IsNullOrEmpty(settings.PreferredTtsProvider))
        {
            _selectedTtsProvider = settings.PreferredTtsProvider;
            OnPropertyChanged(nameof(SelectedTtsProvider));
            OnPropertyChanged(nameof(IsDeepgramTtsSelected));
            OnPropertyChanged(nameof(IsPiperTtsSelected));
        }

        _ttsVolume = settings.TtsVolume * 100.0;
        _ttsSpeed = settings.TtsSpeed;
        _ttsPlaybackService.Volume = (float)settings.TtsVolume;

        OnPropertyChanged(nameof(TtsVolume));
        OnPropertyChanged(nameof(TtsSpeed));

        UpdateVoiceList();
        if (!string.IsNullOrEmpty(settings.SelectedTtsVoice))
        {
            var match = AvailableVoices.FirstOrDefault(v => v.Id.Equals(settings.SelectedTtsVoice, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _selectedVoice = match;
                OnPropertyChanged(nameof(SelectedVoice));
            }
        }

        UpdateLanguageCompatibility();

        if (IsVoiceOutputEnabled && IsPiperTtsSelected)
        {
            _ = EnsurePiperEngineStartedAsync();
        }

        // Initialize hotkey settings and start background polling
        var vKey = settings.HotkeyVirtualKey > 0 ? settings.HotkeyVirtualKey : 0x78; // Default F9
        var mode = Enum.TryParse<HotkeyMode>(settings.HotkeyMode, true, out var parsedMode) ? parsedMode : HotkeyMode.PushToTalk;

        _hotkeyService.Configure(vKey, mode);
        _hotkeyDisplayName = _hotkeyService.KeyDisplayName;
        _isPushToTalkMode = (mode == HotkeyMode.PushToTalk);
        _isToggleMode = (mode == HotkeyMode.Toggle);
        OnPropertyChanged(nameof(HotkeyDisplayName));
        OnPropertyChanged(nameof(IsPushToTalkMode));
        OnPropertyChanged(nameof(IsToggleMode));

        _hotkeyService.Start();

        // Phase 10: Initialize Virtual Audio Routing preferences
        _playTtsThroughSpeakers = settings.PlayTtsThroughSpeakers;
        _routeTtsToVirtualDevice = settings.RouteTtsToVirtualDevice;
        _ttsPlaybackService.PlayThroughSpeakers = settings.PlayTtsThroughSpeakers;
        _ttsPlaybackService.RouteToVirtualDevice = settings.RouteTtsToVirtualDevice;
        _ttsPlaybackService.VirtualDeviceId = settings.VirtualAudioDeviceId;
        _ttsPlaybackService.VirtualDeviceName = settings.VirtualAudioDeviceName;
        OnPropertyChanged(nameof(PlayTtsThroughSpeakers));
        OnPropertyChanged(nameof(RouteTtsToVirtualDevice));

        await LoadVirtualDevicesAsync(settings.VirtualAudioDeviceId, settings.VirtualAudioDeviceName);
    }

    public async Task LoadVirtualDevicesAsync(string? preferredId = null, string? preferredName = null)
    {
        try
        {
            var devices = await _virtualAudioRoutingService.GetPlaybackDevicesAsync();
            AvailableVirtualDevices.Clear();
            foreach (var d in devices)
            {
                AvailableVirtualDevices.Add(d);
            }

            AudioDevice? match = null;
            if (!string.IsNullOrEmpty(preferredId))
            {
                match = devices.FirstOrDefault(d => d.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase));
            }
            if (match == null && !string.IsNullOrEmpty(preferredName))
            {
                match = devices.FirstOrDefault(d => d.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase));
            }
            if (match == null)
            {
                match = await _virtualAudioRoutingService.DetectVirtualAudioDeviceAsync();
            }

            if (match != null)
            {
                _selectedVirtualDevice = match;
                _isVirtualDeviceDetected = true;
                _virtualDeviceStatusText = $"Detected: {match.Name}";
                _ttsPlaybackService.VirtualDeviceId = match.Id;
                _ttsPlaybackService.VirtualDeviceName = match.Name;
            }
            else
            {
                _isVirtualDeviceDetected = false;
                _virtualDeviceStatusText = "VB-CABLE or compatible virtual device not detected.";
            }

            OnPropertyChanged(nameof(SelectedVirtualDevice));
            OnPropertyChanged(nameof(IsVirtualDeviceDetected));
            OnPropertyChanged(nameof(VirtualDeviceStatusText));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load playback endpoints for virtual routing.", ex);
        }
    }

    public async Task DetectVirtualDeviceAsync()
    {
        try
        {
            VirtualDeviceStatusText = "Scanning active playback devices...";
            var devices = await _virtualAudioRoutingService.GetPlaybackDevicesAsync();
            AvailableVirtualDevices.Clear();
            foreach (var d in devices)
            {
                AvailableVirtualDevices.Add(d);
            }

            var detected = await _virtualAudioRoutingService.DetectVirtualAudioDeviceAsync();
            if (detected != null)
            {
                SelectedVirtualDevice = detected;
                IsVirtualDeviceDetected = true;
                VirtualDeviceStatusText = $"Detected: {detected.Name}";
                RouteTtsToVirtualDevice = true; // Auto-enable routing when detected
            }
            else
            {
                IsVirtualDeviceDetected = false;
                VirtualDeviceStatusText = "VB-CABLE or compatible virtual device not detected. Follow setup guide below.";
                IsSetupGuideVisible = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error detecting virtual audio device.", ex);
            VirtualDeviceStatusText = $"Detection error: {ex.Message}";
        }
    }

    private void OpenVbCableDownloadPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://vb-audio.com/Cable/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Could not launch browser for VB-CABLE download page.", ex);
        }
    }

    private async Task SaveRoutingPreferencesAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            settings.PlayTtsThroughSpeakers = PlayTtsThroughSpeakers;
            settings.RouteTtsToVirtualDevice = RouteTtsToVirtualDevice;
            settings.VirtualAudioDeviceId = SelectedVirtualDevice?.Id;
            settings.VirtualAudioDeviceName = SelectedVirtualDevice?.Name;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save virtual audio routing preferences.", ex);
        }
    }

    private void StartRecordingHotkey()
    {
        IsRecordingHotkey = true;
        HotkeyStatusMessage = "Press any key or mouse button (Esc to cancel)...";

        _hotkeyService.StartRecordingNewKey(
            (vKey, keyName) =>
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(async () =>
                {
                    HotkeyDisplayName = keyName;
                    IsRecordingHotkey = false;
                    HotkeyStatusMessage = $"Bound to {keyName}";
                    await SaveHotkeyPreferencesAsync();
                    HotkeyChanged?.Invoke(vKey, keyName, _hotkeyService.Mode);
                });
            },
            () =>
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    IsRecordingHotkey = false;
                    HotkeyStatusMessage = "Recording cancelled.";
                });
            });
    }

    private void CancelRecordingHotkey()
    {
        _hotkeyService.CancelRecording();
        IsRecordingHotkey = false;
        HotkeyStatusMessage = "Recording cancelled.";
    }

    private async Task SaveHotkeyPreferencesAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            settings.HotkeyVirtualKey = _hotkeyService.VirtualKey;
            settings.HotkeyDisplayName = _hotkeyService.KeyDisplayName;
            settings.HotkeyMode = _hotkeyService.Mode.ToString();
            await _settingsService.SaveSettingsAsync(settings);
            Logger.Info($"Saved hotkey preference: {settings.HotkeyDisplayName} ({settings.HotkeyMode})");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to persist hotkey preferences.", ex);
        }
    }

    public async Task LoadMicrophonesAsync()
    {
        try
        {
            var devices = await _microphoneService.GetAudioDevicesAsync();
            var settings = await _settingsService.LoadSettingsAsync();

            AvailableMicrophones.Clear();
            foreach (var d in devices)
            {
                AvailableMicrophones.Add(d);
            }

            if (!string.IsNullOrEmpty(settings.SelectedMicrophoneId))
            {
                _selectedMicrophone = AvailableMicrophones.FirstOrDefault(d => d.Id == settings.SelectedMicrophoneId);
            }

            _selectedMicrophone ??= AvailableMicrophones.FirstOrDefault(d => d.IsDefault) ?? AvailableMicrophones.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedMicrophone));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load microphones in settings.", ex);
        }
    }

    private async Task LoadLanguagePreferencesAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            if (!string.IsNullOrEmpty(settings.DefaultSourceLanguage))
            {
                _selectedSourceLanguage = AvailableSourceLanguages.FirstOrDefault(l => l.Code == settings.DefaultSourceLanguage) ?? _selectedSourceLanguage;
            }
            if (!string.IsNullOrEmpty(settings.DefaultTargetLanguage))
            {
                _selectedTargetLanguage = AvailableTargetLanguages.FirstOrDefault(l => l.Code == settings.DefaultTargetLanguage) ?? _selectedTargetLanguage;
            }

            OnPropertyChanged(nameof(SelectedSourceLanguage));
            OnPropertyChanged(nameof(SelectedTargetLanguage));
            UpdateVoiceList();
            UpdateLanguageCompatibility();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load language preferences in settings.", ex);
        }
    }

    private async Task SaveProviderPreferenceAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            settings.PreferredTranslationProvider = SelectedTranslationProvider;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save provider preference.", ex);
        }
    }

    private async Task SaveTtsPreferencesAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            settings.IsVoiceOutputEnabled = IsVoiceOutputEnabled;
            settings.PreferredTtsProvider = SelectedTtsProvider;
            settings.TtsVolume = TtsVolume / 100.0;
            settings.TtsSpeed = TtsSpeed;
            if (SelectedVoice != null)
            {
                settings.SelectedTtsVoice = SelectedVoice.Id;
            }

            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save TTS preferences.", ex);
        }
    }

    private async Task SaveMicrophonePreferenceAsync(string micId)
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            settings.SelectedMicrophoneId = micId;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save microphone preference.", ex);
        }
    }

    private async Task SaveLanguagePreferenceAsync()
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
            Logger.Error("Failed to save language preferences in settings.", ex);
        }
    }

    #region Translation Provider Pool Methods

    public async Task LoadTranslationPoolAsync()
    {
        try
        {
            var entries = await _translationPoolService.GetPoolEntriesAsync();
            TranslationProviders.Clear();
            foreach (var entry in entries.OrderBy(e => e.Priority))
            {
                var itemVm = new TranslationProviderItemViewModel(
                    entry,
                    onMoveUp: MoveProviderUp,
                    onMoveDown: MoveProviderDown,
                    onDelete: DeleteProvider,
                    onResetUsage: ResetProviderUsage,
                    onChanged: () => _ = SavePoolAsync());

                TranslationProviders.Add(itemVm);
            }
            UpdateOrderCapabilities();
        }
        catch (Exception ex)
        {
            Logger.Error("Error loading translation pool in SettingsViewModel.", ex);
        }
    }

    private void UpdateOrderCapabilities()
    {
        for (int i = 0; i < TranslationProviders.Count; i++)
        {
            TranslationProviders[i].Priority = i + 1;
            TranslationProviders[i].CanMoveUp = (i > 0);
            TranslationProviders[i].CanMoveDown = (i < TranslationProviders.Count - 1);
        }
    }

    private void MoveProviderUp(TranslationProviderItemViewModel item)
    {
        var index = TranslationProviders.IndexOf(item);
        if (index > 0)
        {
            TranslationProviders.Move(index, index - 1);
            UpdateOrderCapabilities();
            _ = SavePoolAsync();
        }
    }

    private void MoveProviderDown(TranslationProviderItemViewModel item)
    {
        var index = TranslationProviders.IndexOf(item);
        if (index >= 0 && index < TranslationProviders.Count - 1)
        {
            TranslationProviders.Move(index, index + 1);
            UpdateOrderCapabilities();
            _ = SavePoolAsync();
        }
    }

    private void DeleteProvider(TranslationProviderItemViewModel item)
    {
        _ = _credentialStore.DeleteApiKeyAsync(item.Id);
        TranslationProviders.Remove(item);
        UpdateOrderCapabilities();
        _ = SavePoolAsync();
    }

    private void ResetProviderUsage(TranslationProviderItemViewModel item)
    {
        _ = _translationPoolService.ResetUsageCounterAsync(item.Id);
        item.CurrentPeriodUsageChars = 0;
        item.RefreshUsage();
    }

    private async Task SavePoolAsync()
    {
        try
        {
            var models = TranslationProviders.Select(vm => vm.GetModel()).ToList();
            await _translationPoolService.SavePoolEntriesAsync(models);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save translation pool entries.", ex);
        }
    }

    private void ShowAddProvider()
    {
        NewProviderType = "GoogleTranslate";
        NewProviderLabel = DefaultPlaceholderLabel;
        NewProviderApiKey = string.Empty;
        NewProviderSoftLimit = 500000;
        AddProviderStatusMessage = string.Empty;
        IsAddingProvider = true;
    }

    private void CancelAddProvider()
    {
        IsAddingProvider = false;
        AddProviderStatusMessage = string.Empty;
        NewProviderApiKey = string.Empty;
    }

    private async Task TestAndSaveNewProviderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProviderLabel))
        {
            AddProviderStatusMessage = "Please specify a display label for this provider.";
            return;
        }

        if (IsNewProviderRequiresKey && string.IsNullOrWhiteSpace(NewProviderApiKey))
        {
            AddProviderStatusMessage = "Please enter an API key.";
            return;
        }

        IsAddProviderTesting = true;
        AddProviderStatusMessage = "Testing credential connection...";

        try
        {
            if (NewProviderType == "GoogleTranslate")
            {
                var validation = await _googleValidator.ValidateApiKeyAsync(NewProviderApiKey.Trim());
                if (!validation.IsSuccess)
                {
                    AddProviderStatusMessage = $"Google validation failed: {validation.Message}";
                    return;
                }
            }
            else if (NewProviderType == "Lecto")
            {
                var validation = await _lectoValidator.ValidateApiKeyAsync(NewProviderApiKey.Trim());
                if (!validation.IsSuccess)
                {
                    AddProviderStatusMessage = $"Lecto validation failed: {validation.Message}";
                    return;
                }
            }

            var newEntry = new TranslationProviderEntry
            {
                Id = Guid.NewGuid().ToString(),
                ProviderType = NewProviderType,
                Label = NewProviderLabel.Trim(),
                Priority = TranslationProviders.Count + 1,
                IsEnabled = true,
                SoftUsageLimitChars = (NewProviderSoftLimit.HasValue && NewProviderSoftLimit.Value > 0) ? NewProviderSoftLimit : null,
                CurrentPeriodUsageChars = 0,
                UsagePeriodStartDate = DateTime.UtcNow
            };

            if (IsNewProviderRequiresKey)
            {
                await _credentialStore.SaveApiKeyAsync(newEntry.Id, NewProviderApiKey.Trim());
            }

            var itemVm = new TranslationProviderItemViewModel(
                newEntry,
                onMoveUp: MoveProviderUp,
                onMoveDown: MoveProviderDown,
                onDelete: DeleteProvider,
                onResetUsage: ResetProviderUsage,
                onChanged: () => _ = SavePoolAsync());

            TranslationProviders.Add(itemVm);
            UpdateOrderCapabilities();
            await SavePoolAsync();

            IsAddingProvider = false;
            NewProviderApiKey = string.Empty;
            AddProviderStatusMessage = string.Empty;
            Logger.Info($"Successfully added provider '{newEntry.Label}' ({newEntry.ProviderType}) to pool.");
        }
        catch (Exception ex)
        {
            Logger.Error("Error testing or adding provider.", ex);
            AddProviderStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsAddProviderTesting = false;
        }
    }

    #endregion
}
