using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using GamingLiveTranslator.Services.Audio;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Services.Hotkeys;
using GamingLiveTranslator.Services.Speech;
using GamingLiveTranslator.Services.TextToSpeech;
using GamingLiveTranslator.Services.Translation;
using GamingLiveTranslator.Services.Updates;
using GamingLiveTranslator.Utilities;
using GamingLiveTranslator.Views;

namespace GamingLiveTranslator.ViewModels;

/// <summary>
/// Root ViewModel orchestrating page-level navigation, shared service lifecycles,
/// and OverlayWindow management across the application.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentView;
    private OverlayWindow? _overlayWindow;
    private readonly SettingsService _settingsService;
    private readonly IUpdateCheckService _updateCheckService;

    // Update Notification Banner state
    private bool _isUpdateNoticeVisible;
    private string _updateNoticeMessage = string.Empty;
    private string _releasePageUrl = "https://github.com/kunsahil42-cpu/translator/releases";
    private string? _detectedLatestVersion;

    public bool IsUpdateNoticeVisible
    {
        get => _isUpdateNoticeVisible;
        set => SetProperty(ref _isUpdateNoticeVisible, value);
    }

    public string UpdateNoticeMessage
    {
        get => _updateNoticeMessage;
        set => SetProperty(ref _updateNoticeMessage, value);
    }

    public string ReleasePageUrl
    {
        get => _releasePageUrl;
        set => SetProperty(ref _releasePageUrl, value);
    }

    public string AppVersionDisplay => $"v{_updateCheckService.CurrentVersion}";

    public DashboardViewModel DashboardVM { get; }
    public TranslatorViewModel TranslatorVM { get; }
    public OverlayViewModel OverlayVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public ViewModelBase CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public ICommand NavigateToDashboardCommand { get; }
    public ICommand NavigateToTranslatorCommand { get; }
    public ICommand NavigateToOverlayCommand { get; }
    public ICommand NavigateToSettingsCommand { get; }

    public ICommand ViewReleaseCommand { get; }
    public ICommand DismissUpdateNoticeCommand { get; }

    public MainViewModel()
    {
        Title = "Gaming Live Translator";

        // Shared service singletons
        var credentialStore = new SecureCredentialStore();
        var microphoneService = new MicrophoneService();
        var speechService = new DeepgramSpeechService();
        var translationService = new TranslationService(credentialStore);
        var lectoTranslationService = new LectoTranslationService(credentialStore);
        var lectoValidator = new LectoTranslateValidator();
        var settingsService = new SettingsService();
        _settingsService = settingsService;
        var updateCheckService = new UpdateCheckService();
        _updateCheckService = updateCheckService;
        var argosProcessManager = new ArgosProcessManager();
        var argosTranslationService = new LocalArgosTranslationService(argosProcessManager);
        var translationPoolService = new TranslationPoolService(
            settingsService,
            credentialStore,
            translationService,
            lectoTranslationService,
            argosTranslationService);
        var piperProcessManager = new PiperProcessManager();
        var deepgramTtsService = new DeepgramTtsService(credentialStore);
        var piperTtsService = new PiperTtsService(piperProcessManager);
        var edgeTtsService = new EdgeTtsService(piperProcessManager);
        var elevenLabsTtsService = new ElevenLabsTtsService(credentialStore);
        var elevenLabsValidator = new ElevenLabsValidator();
        var compositeTtsService = new TextToSpeechService(deepgramTtsService, piperTtsService, edgeTtsService, elevenLabsTtsService, settingsService);
        var ttsPlaybackService = new TtsPlaybackService();
        var hotkeyService = new GlobalHotkeyService();
        var virtualAudioRoutingService = new VirtualAudioRoutingService();

        TranslatorVM = new TranslatorViewModel(
            microphoneService,
            speechService,
            translationService,
            argosTranslationService,
            credentialStore,
            settingsService,
            compositeTtsService,
            ttsPlaybackService,
            hotkeyService,
            translationPoolService);
        
        // Pass shared transcript pipeline to OverlayViewModel
        OverlayVM = new OverlayViewModel(TranslatorVM.TranscriptHistory, settingsService);
        OverlayVM.RequestOverlayVisibilityChanged += HandleOverlayVisibilityChanged;

        // Keep HUD Overlay in sync with active recording/listening state (both UI and Hotkey triggered)
        TranslatorVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TranslatorViewModel.IsListening))
            {
                OverlayVM.IsListening = TranslatorVM.IsListening;
            }
        };

        SettingsVM = new SettingsViewModel(
            credentialStore,
            settingsService: settingsService,
            microphoneService: microphoneService,
            argosProcessManager: argosProcessManager,
            argosTranslationService: argosTranslationService,
            piperProcessManager: piperProcessManager,
            deepgramTtsService: deepgramTtsService,
            piperTtsService: piperTtsService,
            edgeTtsService: edgeTtsService,
            elevenLabsTtsService: elevenLabsTtsService,
            elevenLabsValidator: elevenLabsValidator,
            ttsPlaybackService: ttsPlaybackService,
            hotkeyService: hotkeyService,
            translationPoolService: translationPoolService,
            lectoValidator: lectoValidator,
            virtualAudioRoutingService: virtualAudioRoutingService,
            updateCheckService: updateCheckService);

        DashboardVM = new DashboardViewModel(
            navigateToTranslator: () => CurrentView = TranslatorVM,
            microphoneService: microphoneService,
            speechService: speechService,
            settingsService: settingsService,
            translationPoolService: translationPoolService,
            ttsPlaybackService: ttsPlaybackService);

        // Keep Dashboard reactive to translation provider & hotkey switches in Settings
        SettingsVM.TranslationProviderChanged += (provider) => DashboardVM.UpdateProvider(provider);
        SettingsVM.HotkeyChanged += (vKey, keyName, mode) => DashboardVM.UpdateHotkey(keyName, mode.ToString());

        // Ensure child processes, jobs, and timers are terminated on application exit
        if (Application.Current != null)
        {
            Application.Current.Exit += (s, e) =>
            {
                hotkeyService.Dispose();
                argosProcessManager.Dispose();
                piperProcessManager.Dispose();
                ttsPlaybackService.Dispose();
                updateCheckService.Dispose();
            };
        }

        _currentView = DashboardVM;

        NavigateToDashboardCommand = new RelayCommand(() => CurrentView = DashboardVM);
        NavigateToTranslatorCommand = new RelayCommand(() => CurrentView = TranslatorVM);
        NavigateToOverlayCommand = new RelayCommand(() => CurrentView = OverlayVM);
        NavigateToSettingsCommand = new RelayCommand(() => CurrentView = SettingsVM);

        ViewReleaseCommand = new RelayCommand(ExecuteViewRelease);
        DismissUpdateNoticeCommand = new RelayCommand(async () => await DismissUpdateNoticeAsync());

        // Check if overlay was enabled on previous session
        _ = InitializeOverlayAsync(settingsService);

        // Non-blocking fire-and-forget update check on startup
        _ = CheckForUpdatesOnStartupAsync();
    }

    private async Task InitializeOverlayAsync(SettingsService settingsService)
    {
        try
        {
            var settings = await settingsService.LoadSettingsAsync();
            if (settings.IsOverlayEnabled)
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    HandleOverlayVisibilityChanged(true);
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to initialize overlay on startup.", ex);
        }
    }

    private void HandleOverlayVisibilityChanged(bool isVisible)
    {
        try
        {
            if (isVisible)
            {
                if (_overlayWindow == null)
                {
                    _overlayWindow = new OverlayWindow(OverlayVM);
                    _overlayWindow.Closed += (s, e) => _overlayWindow = null;
                }
                _overlayWindow.Show();
            }
            else
            {
                _overlayWindow?.Hide();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to toggle overlay window visibility.", ex);
            OverlayVM.IsOverlayEnabled = false;
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            // Give the main UI thread 1.5s to finish rendering and initialization
            await Task.Delay(1500);

            var settings = await _settingsService.LoadSettingsAsync();
            if (!settings.CheckForUpdatesOnStartup)
            {
                Logger.Info("[UpdateCheck] Startup update check is disabled by user preference.");
                return;
            }

            var result = await _updateCheckService.CheckForUpdateAsync();
            if (result == null || !result.IsUpdateAvailable)
            {
                return;
            }

            // If user already dismissed this specific version, do not nag again
            if (!string.IsNullOrWhiteSpace(settings.DismissedUpdateVersion) &&
                string.Equals(result.LatestVersion, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"[UpdateCheck] Update v{result.LatestVersion} was previously dismissed by the user.");
                return;
            }

            _detectedLatestVersion = result.LatestVersion;
            UpdateNoticeMessage = $"v{result.LatestVersion} is now available (you're on v{result.CurrentVersion})";
            ReleasePageUrl = result.ReleaseUrl;
            IsUpdateNoticeVisible = true;

            // Remember latest seen version in settings
            settings.LastCheckedLatestVersion = result.LatestVersion;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            // Must never crash or surface errors during startup check
            Logger.Info($"[UpdateCheck] Startup update check completed with notice: {ex.Message}");
        }
    }

    private void ExecuteViewRelease()
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(ReleasePageUrl)
                ? "https://github.com/kunsahil42-cpu/translator/releases"
                : ReleasePageUrl;

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to open GitHub release URL.", ex);
        }
    }

    private async Task DismissUpdateNoticeAsync()
    {
        IsUpdateNoticeVisible = false;

        try
        {
            if (!string.IsNullOrWhiteSpace(_detectedLatestVersion))
            {
                var settings = await _settingsService.LoadSettingsAsync();
                settings.DismissedUpdateVersion = _detectedLatestVersion;
                await _settingsService.SaveSettingsAsync(settings);
                Logger.Info($"[UpdateCheck] Persisted dismissed version: {_detectedLatestVersion}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to persist dismissed update version.", ex);
        }
    }
}
