using System.Windows.Input;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.ViewModels;

/// <summary>
/// ViewModel wrapping an individual TranslationProviderEntry for display and manipulation in Settings.
/// </summary>
public class TranslationProviderItemViewModel : ViewModelBase
{
    private readonly TranslationProviderEntry _entry;
    private readonly Action<TranslationProviderItemViewModel> _onMoveUp;
    private readonly Action<TranslationProviderItemViewModel> _onMoveDown;
    private readonly Action<TranslationProviderItemViewModel> _onDelete;
    private readonly Action<TranslationProviderItemViewModel> _onResetUsage;
    private readonly Action _onChanged;

    public string Id => _entry.Id;
    public string ProviderType => _entry.ProviderType;

    public string Label
    {
        get => _entry.Label;
        set
        {
            if (_entry.Label != value)
            {
                _entry.Label = value;
                OnPropertyChanged(nameof(Label));
                _onChanged();
            }
        }
    }

    public int Priority
    {
        get => _entry.Priority;
        set
        {
            if (_entry.Priority != value)
            {
                _entry.Priority = value;
                OnPropertyChanged(nameof(Priority));
                OnPropertyChanged(nameof(DisplayPriority));
            }
        }
    }

    public string DisplayPriority => $"{Priority}.";

    public bool IsEnabled
    {
        get => _entry.IsEnabled;
        set
        {
            if (_entry.IsEnabled != value)
            {
                _entry.IsEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(StatusToggleText));
                _onChanged();
            }
        }
    }

    public string StatusToggleText => IsEnabled ? "● On" : "○ Off";

    public string TypeBadge => _entry.ProviderType switch
    {
        "GoogleTranslate" => "[Google]",
        "Lecto" => "[Lecto]",
        "ArgosTranslate" => "[Argos]",
        _ => $"[{_entry.ProviderType}]"
    };

    public long? SoftUsageLimitChars
    {
        get => _entry.SoftUsageLimitChars;
        set
        {
            if (_entry.SoftUsageLimitChars != value)
            {
                _entry.SoftUsageLimitChars = value;
                OnPropertyChanged(nameof(SoftUsageLimitChars));
                OnPropertyChanged(nameof(UsageSummaryText));
                _onChanged();
            }
        }
    }

    public long CurrentPeriodUsageChars
    {
        get => _entry.CurrentPeriodUsageChars;
        set
        {
            if (_entry.CurrentPeriodUsageChars != value)
            {
                _entry.CurrentPeriodUsageChars = value;
                OnPropertyChanged(nameof(CurrentPeriodUsageChars));
                OnPropertyChanged(nameof(UsageSummaryText));
            }
        }
    }

    public string UsageSummaryText
    {
        get
        {
            var used = FormatCharCount(_entry.CurrentPeriodUsageChars);
            if (_entry.SoftUsageLimitChars.HasValue && _entry.SoftUsageLimitChars.Value > 0)
            {
                var limit = FormatCharCount(_entry.SoftUsageLimitChars.Value);
                return $"~{used} / {limit} chars this month";
            }
            return $"~{used} chars this month (no limit set)";
        }
    }

    public bool CanMoveUp { get; set; }
    public bool CanMoveDown { get; set; }

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ToggleEnabledCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ResetUsageCommand { get; }

    public TranslationProviderItemViewModel(
        TranslationProviderEntry entry,
        Action<TranslationProviderItemViewModel> onMoveUp,
        Action<TranslationProviderItemViewModel> onMoveDown,
        Action<TranslationProviderItemViewModel> onDelete,
        Action<TranslationProviderItemViewModel> onResetUsage,
        Action onChanged)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _onMoveUp = onMoveUp ?? throw new ArgumentNullException(nameof(onMoveUp));
        _onMoveDown = onMoveDown ?? throw new ArgumentNullException(nameof(onMoveDown));
        _onDelete = onDelete ?? throw new ArgumentNullException(nameof(onDelete));
        _onResetUsage = onResetUsage ?? throw new ArgumentNullException(nameof(onResetUsage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        MoveUpCommand = new RelayCommand(() => _onMoveUp(this), () => CanMoveUp);
        MoveDownCommand = new RelayCommand(() => _onMoveDown(this), () => CanMoveDown);
        ToggleEnabledCommand = new RelayCommand(() => IsEnabled = !IsEnabled);
        DeleteCommand = new RelayCommand(() => _onDelete(this));
        ResetUsageCommand = new RelayCommand(() => _onResetUsage(this));
    }

    public TranslationProviderEntry GetModel() => _entry;

    public void RefreshUsage()
    {
        OnPropertyChanged(nameof(CurrentPeriodUsageChars));
        OnPropertyChanged(nameof(UsageSummaryText));
    }

    private static string FormatCharCount(long count)
    {
        if (count >= 1_000_000)
            return $"{(count / 1_000_000.0):0.#}M";
        if (count >= 1_000)
            return $"{(count / 1_000.0):0.#}K";
        return count.ToString();
    }
}
