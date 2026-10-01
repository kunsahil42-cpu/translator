using System.Windows.Input;
using GamingLiveTranslator.Models;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Services.Speech;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.ViewModels;

/// <summary>
/// Reusable ViewModel encapsulating API key entry, masking, DPAPI secure storage,
/// and live connection verification for any third-party provider (Deepgram, Google, etc.).
/// </summary>
public class ProviderCredentialViewModel : ViewModelBase
{
    private readonly ISecureCredentialStore _credentialStore;
    private readonly Func<string, Task<ApiValidationResult>> _validator;

    private string _apiKeyInput = string.Empty;
    private bool _isKeyVisible;
    private ConnectionStatus _connectionStatus = ConnectionStatus.NotTested;
    private string _connectionStatusMessage = "Not tested";
    private bool _isBusy;

    public string ProviderKey { get; }
    public string SectionTitle { get; }
    public string KeyLabel { get; }
    public string HelpText { get; }

    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set
        {
            if (SetProperty(ref _apiKeyInput, value))
            {
                if (ConnectionStatus != ConnectionStatus.NotTested)
                {
                    ConnectionStatus = ConnectionStatus.NotTested;
                    ConnectionStatusMessage = "Key modified — not tested";
                }
            }
        }
    }

    public bool IsKeyVisible
    {
        get => _isKeyVisible;
        set => SetProperty(ref _isKeyVisible, value);
    }

    public ConnectionStatus ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public string ConnectionStatusMessage
    {
        get => _connectionStatusMessage;
        set => SetProperty(ref _connectionStatusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ICommand TestConnectionCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ClearKeyCommand { get; }
    public ICommand ToggleKeyVisibilityCommand { get; }

    public ProviderCredentialViewModel(
        ISecureCredentialStore credentialStore,
        Func<string, Task<ApiValidationResult>> validator,
        string providerKey,
        string sectionTitle,
        string keyLabel,
        string helpText)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        ProviderKey = providerKey;
        SectionTitle = sectionTitle;
        KeyLabel = keyLabel;
        HelpText = helpText;

        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync(), () => !IsBusy);
        SaveCommand = new RelayCommand(async () => await SaveKeyAsync(), () => !IsBusy);
        ClearKeyCommand = new RelayCommand(async () => await ClearKeyAsync(), () => !IsBusy);
        ToggleKeyVisibilityCommand = new RelayCommand(() => IsKeyVisible = !IsKeyVisible);

        _ = LoadStoredKeyAsync();
    }

    public async Task LoadStoredKeyAsync()
    {
        try
        {
            var savedKey = await _credentialStore.GetApiKeyAsync(ProviderKey);
            if (!string.IsNullOrEmpty(savedKey))
            {
                _apiKeyInput = savedKey;
                OnPropertyChanged(nameof(ApiKeyInput));
                ConnectionStatus = ConnectionStatus.NotTested;
                ConnectionStatusMessage = "Key loaded from secure storage (not tested)";
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error loading stored API key for '{ProviderKey}'.", ex);
        }
    }

    public async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            ConnectionStatus = ConnectionStatus.Failed;
            ConnectionStatusMessage = "API key cannot be empty or whitespace.";
            return;
        }

        IsBusy = true;
        ConnectionStatus = ConnectionStatus.Testing;
        ConnectionStatusMessage = "Testing connection...";

        try
        {
            var result = await _validator(ApiKeyInput.Trim());

            if (result.IsSuccess)
            {
                ConnectionStatus = ConnectionStatus.Connected;
                ConnectionStatusMessage = "Connected successfully";
                await _credentialStore.SaveApiKeyAsync(ProviderKey, ApiKeyInput.Trim());
            }
            else
            {
                ConnectionStatus = ConnectionStatus.Failed;
                ConnectionStatusMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Connection test failed for '{ProviderKey}'.", ex);
            ConnectionStatus = ConnectionStatus.Failed;
            ConnectionStatusMessage = "An unexpected error occurred during test.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveKeyAsync()
    {
        IsBusy = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(ApiKeyInput))
            {
                await _credentialStore.SaveApiKeyAsync(ProviderKey, ApiKeyInput.Trim());
                ConnectionStatusMessage = "Key saved securely";
            }
            else
            {
                ConnectionStatusMessage = "Key field is empty";
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save key for '{ProviderKey}'.", ex);
            ConnectionStatus = ConnectionStatus.Failed;
            ConnectionStatusMessage = "Failed to save key.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ClearKeyAsync()
    {
        IsBusy = true;
        try
        {
            await _credentialStore.DeleteApiKeyAsync(ProviderKey);
            ApiKeyInput = string.Empty;
            ConnectionStatus = ConnectionStatus.NotTested;
            ConnectionStatusMessage = "Key removed from secure storage";
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to clear key for '{ProviderKey}'.", ex);
            ConnectionStatus = ConnectionStatus.Failed;
            ConnectionStatusMessage = "Failed to clear key.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
