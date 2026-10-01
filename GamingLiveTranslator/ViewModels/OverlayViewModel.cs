using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.ViewModels;

/// <summary>
/// ViewModel managing the In-Game HUD Overlay state, settings, message visibility,
/// and leak-free age-based pruning.
/// </summary>
public class OverlayViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly ObservableCollection<TranslationMessage>? _sharedHistory;
    private readonly DispatcherTimer _pruneTimer;

    private bool _isOverlayEnabled;
    private bool _isClickThrough;
    private bool _isListening;
    private double _opacity = 0.85;
    private double _fontSize = 15.0;
    private int _selectedDuration = 8;
    private int _selectedMaxMessages = 5;

    public bool IsListening
    {
        get => _isListening;
        set => SetProperty(ref _isListening, value);
    }

    // Window coordinate state (persisted)
    private double? _overlayLeft;
    private double? _overlayTop;
    private double? _overlayWidth;
    private double? _overlayHeight;

    public ObservableCollection<TranslationMessage> VisibleMessages { get; } = new();

    public IReadOnlyList<int> AvailableDurations { get; } = new[] { 4, 6, 8, 10, 15, 20, 30 };
    public IReadOnlyList<int> AvailableMaxMessages { get; } = new[] { 2, 3, 5, 7, 10 };

    public event Action<bool>? RequestOverlayVisibilityChanged;
    public event Action<bool>? RequestClickThroughChanged;
    public event Action? RequestResetPosition;

    public bool IsOverlayEnabled
    {
        get => _isOverlayEnabled;
        set
        {
            if (SetProperty(ref _isOverlayEnabled, value))
            {
                RequestOverlayVisibilityChanged?.Invoke(value);
                _ = SaveSettingsAsync();
            }
        }
    }

    public bool IsClickThrough
    {
        get => _isClickThrough;
        set
        {
            if (SetProperty(ref _isClickThrough, value))
            {
                // TODO: Phase 8 will add a global hotkey to toggle click-through without alt-tabbing
                RequestClickThroughChanged?.Invoke(value);
                _ = SaveSettingsAsync();
            }
        }
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            if (SetProperty(ref _opacity, Math.Clamp(value, 0.2, 1.0)))
            {
                _ = SaveSettingsAsync();
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (SetProperty(ref _fontSize, Math.Clamp(value, 11.0, 26.0)))
            {
                _ = SaveSettingsAsync();
            }
        }
    }

    public int SelectedDuration
    {
        get => _selectedDuration;
        set
        {
            if (SetProperty(ref _selectedDuration, value))
            {
                _ = SaveSettingsAsync();
            }
        }
    }

    public int SelectedMaxMessages
    {
        get => _selectedMaxMessages;
        set
        {
            if (SetProperty(ref _selectedMaxMessages, value))
            {
                TrimVisibleMessages();
                _ = SaveSettingsAsync();
            }
        }
    }

    public double? OverlayLeft
    {
        get => _overlayLeft;
        set => SetProperty(ref _overlayLeft, value);
    }

    public double? OverlayTop
    {
        get => _overlayTop;
        set => SetProperty(ref _overlayTop, value);
    }

    public double? OverlayWidth
    {
        get => _overlayWidth;
        set => SetProperty(ref _overlayWidth, value);
    }

    public double? OverlayHeight
    {
        get => _overlayHeight;
        set => SetProperty(ref _overlayHeight, value);
    }

    public ICommand ToggleOverlayCommand { get; }
    public ICommand ToggleClickThroughCommand { get; }
    public ICommand ResetPositionCommand { get; }

    public OverlayViewModel(
        ObservableCollection<TranslationMessage>? sharedHistory = null,
        SettingsService? settingsService = null)
    {
        Title = "HUD Overlay";
        _settingsService = settingsService ?? new SettingsService();
        _sharedHistory = sharedHistory;

        ToggleOverlayCommand = new RelayCommand(() => IsOverlayEnabled = !IsOverlayEnabled);
        ToggleClickThroughCommand = new RelayCommand(() => IsClickThrough = !IsClickThrough);
        ResetPositionCommand = new RelayCommand(ResetPosition);

        // Single non-leaking DispatcherTimer for age-based pruning
        _pruneTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _pruneTimer.Tick += OnPruneTimerTick;
        _pruneTimer.Start();

        // Observe shared transcript history from TranslatorViewModel
        if (_sharedHistory != null)
        {
            _sharedHistory.CollectionChanged += OnSharedHistoryChanged;
        }

        _ = LoadSettingsAsync();
    }

    private void OnSharedHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                foreach (TranslationMessage msg in e.NewItems)
                {
                    // Add new message at the bottom (or top) of the overlay stream
                    VisibleMessages.Add(msg);
                }

                TrimVisibleMessages();
            });
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                VisibleMessages.Clear();
            });
        }
    }

    private void OnPruneTimerTick(object? sender, EventArgs e)
    {
        if (VisibleMessages.Count == 0)
            return;

        var threshold = DateTime.Now - TimeSpan.FromSeconds(SelectedDuration);
        for (int i = VisibleMessages.Count - 1; i >= 0; i--)
        {
            if (VisibleMessages[i].Timestamp < threshold)
            {
                VisibleMessages.RemoveAt(i);
            }
        }
    }

    private void TrimVisibleMessages()
    {
        while (VisibleMessages.Count > SelectedMaxMessages)
        {
            VisibleMessages.RemoveAt(0); // Drop oldest
        }
    }

    public void UpdateBounds(double left, double top, double width, double height)
    {
        _overlayLeft = left;
        _overlayTop = top;
        _overlayWidth = width;
        _overlayHeight = height;
        _ = SaveSettingsAsync();
    }

    private void ResetPosition()
    {
        _overlayLeft = null;
        _overlayTop = null;
        _overlayWidth = null;
        _overlayHeight = null;
        RequestResetPosition?.Invoke();
        _ = SaveSettingsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            _isOverlayEnabled = settings.IsOverlayEnabled;
            _isClickThrough = settings.IsOverlayClickThrough;
            _opacity = settings.OverlayOpacity;
            _fontSize = settings.OverlayFontSize;
            _selectedDuration = settings.OverlayDurationSeconds;
            _selectedMaxMessages = settings.OverlayMaxMessages;
            _overlayLeft = settings.OverlayLeft;
            _overlayTop = settings.OverlayTop;
            _overlayWidth = settings.OverlayWidth;
            _overlayHeight = settings.OverlayHeight;

            OnPropertyChanged(nameof(IsOverlayEnabled));
            OnPropertyChanged(nameof(IsClickThrough));
            OnPropertyChanged(nameof(Opacity));
            OnPropertyChanged(nameof(FontSize));
            OnPropertyChanged(nameof(SelectedDuration));
            OnPropertyChanged(nameof(SelectedMaxMessages));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load overlay settings.", ex);
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            settings.IsOverlayEnabled = IsOverlayEnabled;
            settings.IsOverlayClickThrough = IsClickThrough;
            settings.OverlayOpacity = Opacity;
            settings.OverlayFontSize = FontSize;
            settings.OverlayDurationSeconds = SelectedDuration;
            settings.OverlayMaxMessages = SelectedMaxMessages;
            settings.OverlayLeft = OverlayLeft;
            settings.OverlayTop = OverlayTop;
            settings.OverlayWidth = OverlayWidth;
            settings.OverlayHeight = OverlayHeight;

            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save overlay settings.", ex);
        }
    }
}
