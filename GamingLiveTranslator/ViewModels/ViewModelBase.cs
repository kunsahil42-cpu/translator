using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.ViewModels;

/// <summary>
/// Common base class for all application ViewModels.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private string _title = string.Empty;

    public string Title
    {
        get => _title;
        protected set => SetProperty(ref _title, value);
    }
}
