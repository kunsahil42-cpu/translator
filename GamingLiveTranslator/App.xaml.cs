using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using GamingLiveTranslator.Services.Configuration;
using GamingLiveTranslator.Services.TextToSpeech;
using GamingLiveTranslator.Services.Translation;
using GamingLiveTranslator.Services.Updates;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0] == "--verify-credentials")
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            var credStore = new SecureCredentialStore();
            var deepgram = await credStore.GetApiKeyAsync("Deepgram");
            var elevenLabs = await credStore.GetApiKeyAsync("ElevenLabs");
            var google = await credStore.GetApiKeyAsync("GoogleTranslate");
            var settings = await new SettingsService().LoadSettingsAsync();
            Console.WriteLine($"Deepgram Stored Key: '{(string.IsNullOrEmpty(deepgram) ? "[EMPTY]" : "[CONFIGURED]")}'");
            Console.WriteLine($"ElevenLabs Stored Key: '{(string.IsNullOrEmpty(elevenLabs) ? "[EMPTY]" : "[CONFIGURED]")}'");
            Console.WriteLine($"Google Stored Key: '{(string.IsNullOrEmpty(google) ? "[EMPTY]" : "[CONFIGURED]")}'");
            Console.WriteLine($"Translation Pool Count: {settings.TranslationProviderPool?.Count ?? 0}");
            Shutdown(0);
            return;
        }

        if (e.Args.Length > 0 && (e.Args[0] == "--smoke-test" || e.Args[0] == "--test-runtime"))
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            var success = await RunSmokeTestAsync();
            Shutdown(success ? 0 : 1);
            return;
        }

        var mainWindow = new Views.MainWindow();
        mainWindow.Show();
    }

    private static async Task<bool> RunSmokeTestAsync()
    {
        Console.WriteLine("\n======================================================================");
        Console.WriteLine("Gaming Live Translator - Published Package Smoke Test");
        Console.WriteLine($"Base Directory (AppContext.BaseDirectory): {AppContext.BaseDirectory}");
        Console.WriteLine("======================================================================");

        try
        {
            // 1. Verify Argos Translation Subprocess
            Console.WriteLine("\n[1/3] Verifying Local Argos Translation Process Manager...");
            using var argos = new ArgosProcessManager();
            var argosStarted = await argos.EnsureStartedAsync();
            Console.WriteLine($" -> Argos Process Running: {argosStarted}");
            Console.WriteLine($" -> Bound Loopback Port: {argos.Port}");
            Console.WriteLine($" -> Status Message: {argos.StatusMessage}");

            if (!argosStarted)
            {
                Console.WriteLine("[ERROR] Failed to start Argos Translation subprocess.");
                return false;
            }

            var translationService = new LocalArgosTranslationService(argos);
            Console.WriteLine(" -> Sending test translation request ('Hello' en -> hi)...");
            var translationResult = await translationService.TranslateAsync("Hello", "en", "hi");
            Console.WriteLine($" -> Translation Success: {translationResult.IsSuccess}");
            Console.WriteLine($" -> Translated Output: '{translationResult.TranslatedText}'");

            if (!translationResult.IsSuccess)
            {
                Console.WriteLine($"[ERROR] Translation failed: {translationResult.ErrorMessage}");
                return false;
            }

            // 2. Verify Piper TTS Subprocess
            Console.WriteLine("\n[2/3] Verifying Local Piper / Edge TTS Process Manager...");
            using var piper = new PiperProcessManager();
            var piperStarted = await piper.EnsureStartedAsync();
            Console.WriteLine($" -> Piper Process Running: {piperStarted}");
            Console.WriteLine($" -> Bound Loopback Port: {piper.Port}");
            Console.WriteLine($" -> Status Message: {piper.StatusMessage}");

            if (!piperStarted)
            {
                Console.WriteLine("[ERROR] Failed to start Piper TTS subprocess.");
                return false;
            }

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var healthResp = await httpClient.GetStringAsync($"http://127.0.0.1:{piper.Port}/health");
            Console.WriteLine($" -> Piper HTTP Health Response: {healthResp.Trim()}");

            // 3. Verify UpdateCheckService (Semver logic, simulation override, and resilient network call)
            Console.WriteLine("\n[3/3] Verifying GitHub Update Check Service & Semver Logic...");
            var updateChecker = new UpdateCheckService();
            Console.WriteLine($" -> Detected App Version: {updateChecker.CurrentVersion}");

            // Verify SemVer comparisons
            bool testNewer = UpdateCheckService.CompareVersions("1.0.0", "v1.2.0", out var clean1);
            bool testSame = UpdateCheckService.CompareVersions("1.0.0", "1.0.0", out var clean2);
            bool testOlder = UpdateCheckService.CompareVersions("1.2.0", "v1.0.0", out var clean3);
            bool testPrerelease = UpdateCheckService.CompareVersions("1.0.0", "v2.0.0-beta.1", out var clean4);

            Console.WriteLine($" -> Semver Test 1.0.0 vs v1.2.0 (Expect true): {testNewer} (parsed: {clean1})");
            Console.WriteLine($" -> Semver Test 1.0.0 vs 1.0.0 (Expect false): {testSame} (parsed: {clean2})");
            Console.WriteLine($" -> Semver Test 1.2.0 vs v1.0.0 (Expect false): {testOlder} (parsed: {clean3})");
            Console.WriteLine($" -> Semver Test 1.0.0 vs v2.0.0-beta.1 (Expect true): {testPrerelease} (parsed: {clean4})");

            if (!testNewer || testSame || testOlder || !testPrerelease)
            {
                Console.WriteLine("[ERROR] SemVer comparison logic failed test assertions.");
                return false;
            }

            // Verify Simulated Tag flow
            updateChecker.SimulatedTag = "v2.5.0";
            var simResult = await updateChecker.CheckForUpdateAsync();
            Console.WriteLine($" -> Simulated Update Available: {simResult?.IsUpdateAvailable} (Latest: {simResult?.LatestVersion})");
            if (simResult?.IsUpdateAvailable != true || simResult.LatestVersion != "2.5.0")
            {
                Console.WriteLine("[ERROR] Simulated update check did not return expected result.");
                return false;
            }
            updateChecker.SimulatedTag = null; // reset

            // Verify Real Network check (must not throw, returns either result or null gracefully)
            Console.WriteLine(" -> Performing live GitHub check (resilient keyless API)...");
            var liveResult = await updateChecker.CheckForUpdateAsync();
            Console.WriteLine($" -> Live Check Completed Gracefully: Available={liveResult?.IsUpdateAvailable ?? false}");

            Console.WriteLine("\n======================================================================");
            Console.WriteLine("SUCCESS: All subprocesses, path resolutions & update logic verified!");
            Console.WriteLine("======================================================================\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FATAL ERROR during smoke test]: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled Dispatcher Exception", e.Exception);
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails:\n{e.Exception}", 
            "Gaming Live Translator - Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Logger.Error("Unhandled Domain Exception", ex);
            MessageBox.Show($"A critical error occurred:\n\n{ex.Message}\n\nDetails:\n{ex}", 
                "Gaming Live Translator - Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

