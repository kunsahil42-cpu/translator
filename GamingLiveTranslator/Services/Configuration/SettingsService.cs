using System.IO;
using System.Text.Json;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Configuration;

/// <summary>
/// Service for loading and persisting non-sensitive application settings in %LOCALAPPDATA%\GamingLiveTranslator\settings.json.
/// </summary>
public class SettingsService
{
    private readonly string _settingsFilePath;

    public SettingsService(string? settingsFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(localAppData, "GamingLiveTranslator");
            _settingsFilePath = Path.Combine(appFolder, "settings.json");
        }
        else
        {
            _settingsFilePath = settingsFilePath;
        }
    }

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public async Task<ApiSettings> LoadSettingsAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            if (!File.Exists(_settingsFilePath))
                return new ApiSettings();

            var json = await File.ReadAllTextAsync(_settingsFilePath);
            return JsonSerializer.Deserialize<ApiSettings>(json) ?? new ApiSettings();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load settings from JSON. Reverting to default settings.", ex);
            return new ApiSettings();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveSettingsAsync(ApiSettings settings)
    {
        await _fileLock.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(_settingsFilePath, json);
            Logger.Info("Application settings saved successfully.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to persist application settings.", ex);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
