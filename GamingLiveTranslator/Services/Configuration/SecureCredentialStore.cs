using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Configuration;

/// <summary>
/// Secure credential storage using Windows DPAPI (ProtectedData scoped to CurrentUser).
/// Encrypted blobs are stored at %LOCALAPPDATA%\GamingLiveTranslator\secrets.dat.
/// Never logs raw or partial API keys.
/// </summary>
public class SecureCredentialStore : ISecureCredentialStore
{
    private readonly string _secretsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public SecureCredentialStore(string? secretsFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(secretsFilePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(localAppData, "GamingLiveTranslator");
            _secretsFilePath = Path.Combine(appFolder, "secrets.dat");
        }
        else
        {
            _secretsFilePath = secretsFilePath;
        }
    }

    public async Task SaveApiKeyAsync(string providerName, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name cannot be empty.", nameof(providerName));

        await _fileLock.WaitAsync();
        try
        {
            var credentials = await LoadDecryptedMapAsync();
            credentials[providerName] = apiKey;
            await SaveEncryptedMapAsync(credentials);
            Logger.Info($"Securely saved credential for provider '{providerName}'.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to securely save credential for provider '{providerName}'.", ex);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<string?> GetApiKeyAsync(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return null;

        await _fileLock.WaitAsync();
        try
        {
            var credentials = await LoadDecryptedMapAsync();
            return credentials.TryGetValue(providerName, out var key) ? key : null;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to retrieve credential for provider '{providerName}'.", ex);
            return null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task DeleteApiKeyAsync(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return;

        await _fileLock.WaitAsync();
        try
        {
            var credentials = await LoadDecryptedMapAsync();
            if (credentials.Remove(providerName))
            {
                await SaveEncryptedMapAsync(credentials);
                Logger.Info($"Deleted stored credential for provider '{providerName}'.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to delete credential for provider '{providerName}'.", ex);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadDecryptedMapAsync()
    {
        if (!File.Exists(_secretsFilePath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(_secretsFilePath);
            if (encryptedBytes.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decryptedBytes);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) 
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (CryptographicException ex)
        {
            Logger.Warn($"DPAPI decryption failed for secrets file (corrupted or different user context): {ex.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveEncryptedMapAsync(Dictionary<string, string> credentials)
    {
        var directory = Path.GetDirectoryName(_secretsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(credentials);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

        await File.WriteAllBytesAsync(_secretsFilePath, encryptedBytes);
    }
}
