namespace GamingLiveTranslator.Models;

/// <summary>
/// Represents a hardware or virtual audio input/output device.
/// </summary>
public class AudioDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsInput { get; set; } = true;

    public override string ToString() => Name;
}
