namespace GamingLiveTranslator.Models;

/// <summary>
/// Status of an API provider connection verification.
/// </summary>
public enum ConnectionStatus
{
    NotTested,
    Testing,
    Connected,
    Failed
}
