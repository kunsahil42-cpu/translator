using System.Windows;
using System.Windows.Input;
using GamingLiveTranslator.Services.Audio;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Services.Hotkeys;
using GamingLiveTranslator.Services.Speech;
using GamingLiveTranslator.Services.TextToSpeech;
using GamingLiveTranslator.Services.Translation;
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
        var compositeTtsService = new TextToSpeechService(deepgramTtsService, piperTtsService, edgeTtsService, settingsService);
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
            ttsPlaybackService: ttsPlaybackService,
            hotkeyService: hotkeyService,
            translationPoolService: translationPoolService,
            lectoValidator: lectoValidator,
            virtualAudioRoutingService: virtualAudioRoutingService);

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
            };
        }

        _currentView = DashboardVM;

        NavigateToDashboardCommand = new RelayCommand(() => CurrentView = DashboardVM);
        NavigateToTranslatorCommand = new RelayCommand(() => CurrentView = TranslatorVM);
        NavigateToOverlayCommand = new RelayCommand(() => CurrentView = OverlayVM);
        NavigateToSettingsCommand = new RelayCommand(() => CurrentView = SettingsVM);

        // Check if overlay was enabled on previous session
        _ = InitializeOverlayAsync(settingsService);
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
}
