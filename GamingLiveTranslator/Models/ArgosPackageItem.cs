using System.Windows.Input;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Models;

/// <summary>
/// Represents an offline language package pair in the Argos Translate package manager.
/// </summary>
public class ArgosPackageItem : ObservableObject
{
    private bool _isInstalled;
    private bool _isDownloading;
    private string _statusText = "Not installed";

    public string DisplayName { get; }
    public string FromCode { get; }
    public string ToCode { get; }
    public string ApproxSize { get; }

    public bool IsInstalled
    {
        get => _isInstalled;
        set => SetProperty(ref _isInstalled, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ICommand DownloadCommand { get; set; } = null!;

    public ArgosPackageItem(string displayName, string fromCode, string toCode, string approxSize = "~85 MB")
    {
        DisplayName = displayName;
        FromCode = fromCode;
        ToCode = toCode;
        ApproxSize = approxSize;
    }
}
