namespace GamingLiveTranslator.Services.Configuration;

/// <summary>
/// Provider-keyed credential store interface for securely persisting sensitive API tokens.
/// </summary>
public interface ISecureCredentialStore
{
    Task SaveApiKeyAsync(string providerName, string apiKey);
    Task<string?> GetApiKeyAsync(string providerName);
    Task DeleteApiKeyAsync(string providerName);
}
