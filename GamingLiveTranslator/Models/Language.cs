namespace GamingLiveTranslator.Models;

/// <summary>
/// Represents a supported spoken or translated language with ISO-639-1 / BCP-47 codes.
/// </summary>
public class Language
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;

    public Language() { }

    public Language(string code, string displayName, string nativeName = "")
    {
        Code = code;
        DisplayName = displayName;
        NativeName = string.IsNullOrEmpty(nativeName) ? displayName : nativeName;
    }

    public override string ToString() => DisplayName;

    /// <summary>
    /// Verified supported initial languages catalog for Google Cloud Translation & Deepgram.
    /// </summary>
    public static IReadOnlyList<Language> GetInitialLanguages(bool includeAutoDetect = false)
    {
        var list = new List<Language>();

        if (includeAutoDetect)
        {
            list.Add(new Language("auto", "🌐 Auto Detect", "Auto Detect"));
        }

        list.Add(new Language("hi", "🇮🇳 Hindi", "हिन्दी"));
        list.Add(new Language("en", "🇬🇧 English", "English"));
        list.Add(new Language("zh-CN", "🇨🇳 Chinese (Simplified)", "简体中文"));
        list.Add(new Language("zh-TW", "🇹🇼 Chinese (Traditional)", "繁體中文"));
        list.Add(new Language("ja", "🇯🇵 Japanese", "日本語"));
        list.Add(new Language("ko", "🇰🇷 Korean", "한국어"));
        list.Add(new Language("es", "🇪🇸 Spanish", "Español"));
        list.Add(new Language("fr", "🇫🇷 French", "Français"));
        list.Add(new Language("de", "🇩🇪 German", "Deutsch"));
        list.Add(new Language("pt", "🇵🇹 Portuguese", "Português"));
        list.Add(new Language("ru", "🇷🇺 Russian", "Русский"));
        list.Add(new Language("ar", "🇸🇦 Arabic", "العربية"));
        list.Add(new Language("th", "🇹🇭 Thai", "ไทย"));

        return list;
    }
}
